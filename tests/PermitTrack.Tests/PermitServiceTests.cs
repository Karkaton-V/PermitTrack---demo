using Microsoft.AspNetCore.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using PermitTrack.Web.Data;
using PermitTrack.Web.Domain;
using PermitTrack.Web.Services;

namespace PermitTrack.Tests;

/// <summary>End-to-end service tests against a real (in-memory) SQLite database.</summary>
public sealed class PermitServiceTests : IDisposable
{
    // Tuesday, so "tomorrow" is a weekday.
    private static readonly DateTime Now = new(2026, 10, 27, 9, 0, 0);
    private static readonly DateOnly Tomorrow = new(2026, 10, 28);

    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly AppDbContext _db;
    private readonly PermitService _service;
    private readonly CurrentUser _applicant, _clerk, _inspectorA, _inspectorB;

    public PermitServiceTests()
    {
        _connection.Open();
        _db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();

        var users = new[]
        {
            new AppUser { Name = "Applicant", Email = "a@x", Role = UserRole.Applicant },
            new AppUser { Name = "Clerk", Email = "c@x", Role = UserRole.Clerk },
            new AppUser { Name = "Inspector A", Email = "ia@x", Role = UserRole.Inspector },
            new AppUser { Name = "Inspector B", Email = "ib@x", Role = UserRole.Inspector },
        };
        _db.Users.AddRange(users);
        _db.SaveChanges();
        CurrentUser As(AppUser u) => new(u.Id, u.Name, u.Role);
        (_applicant, _clerk, _inspectorA, _inspectorB) = (As(users[0]), As(users[1]), As(users[2]), As(users[3]));

        _service = new PermitService(_db, new FixedClock(Now), new FakeEnvironment());
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private async Task<Permit> IssuedElectricalPermitAsync()
    {
        var permit = await _service.CreateDraftAsync(
            new PermitInput(PermitType.Electrical, "12 Oak St", null, "New 20A circuit", 1_500m, null, null), _applicant);
        permit.Documents.Add(new PermitDocument { FileName = "a.pdf", StoredName = "a.pdf", ContentType = "application/pdf", UploadedById = _applicant.Id, UploadedAt = Now });
        await _db.SaveChangesAsync();

        Assert.True((await _service.ApplyActionAsync(permit, PermitAction.Submit, _applicant, null)).Ok);
        Assert.True((await _service.ApplyActionAsync(permit, PermitAction.StartReview, _clerk, null)).Ok);
        Assert.True((await _service.RecordPaymentAsync(permit, "R-1", _clerk)).Ok);
        Assert.True((await _service.ApplyActionAsync(permit, PermitAction.Approve, _clerk, null)).Ok);
        return permit;
    }

    [Fact]
    public async Task Draft_gets_sequential_number_and_calculated_fee()
    {
        var input = new PermitInput(PermitType.Building, "1 Main St", null, "Deck addition", 18_500m, null, null);
        var first = await _service.CreateDraftAsync(input, _applicant);
        var second = await _service.CreateDraftAsync(input, _applicant);

        Assert.Equal("B-2026-0001", first.Number);
        Assert.Equal("B-2026-0002", second.Number);
        Assert.Equal(FeeCalculator.Calculate(PermitType.Building, 18_500m).Total, first.FeeTotal);
        Assert.Equal(PermitStatus.Draft, first.Status);
    }

    [Fact]
    public async Task Full_lifecycle_finals_permit_when_last_inspection_passes()
    {
        var permit = await IssuedElectricalPermitAsync();
        Assert.Equal(PermitStatus.Issued, permit.Status);
        Assert.Equal(_clerk.Id, permit.ReviewerId);

        Assert.True((await _service.ScheduleInspectionAsync(permit, InspectionType.RoughElectrical, Tomorrow, _applicant)).Ok);
        var rough = permit.Inspections.Single();
        Assert.True((await _service.RecordResultAsync(rough, InspectionResult.Passed, null, null, null, Assigned(rough))).Ok);
        Assert.Equal(PermitStatus.Issued, permit.Status);

        Assert.True((await _service.ScheduleInspectionAsync(permit, InspectionType.FinalElectrical, Tomorrow, _applicant)).Ok);
        var final = permit.Inspections.Single(i => i.Type == InspectionType.FinalElectrical);
        Assert.True((await _service.RecordResultAsync(final, InspectionResult.Passed, "Good", null, null, Assigned(final))).Ok);

        Assert.Equal(PermitStatus.Finaled, permit.Status);
        Assert.NotNull(permit.FinaledAt);
        Assert.Contains(permit.Events, e => e.Action == "Issued → Finaled");
    }

    [Fact]
    public async Task Inspections_are_assigned_to_least_busy_inspector()
    {
        var p1 = await IssuedElectricalPermitAsync();
        var p2 = await IssuedElectricalPermitAsync();
        var p3 = await IssuedElectricalPermitAsync();

        await _service.ScheduleInspectionAsync(p1, InspectionType.RoughElectrical, Tomorrow, _applicant);
        await _service.ScheduleInspectionAsync(p2, InspectionType.RoughElectrical, Tomorrow, _applicant);
        await _service.ScheduleInspectionAsync(p3, InspectionType.RoughElectrical, Tomorrow, _applicant);

        var counts = await _db.Inspections.GroupBy(i => i.InspectorId).Select(g => g.Count()).ToListAsync();
        Assert.Equal(2, counts.Count);
        Assert.Equal([1, 2], counts.Order());
    }

    [Fact]
    public async Task Scheduling_rejects_weekends_and_same_day()
    {
        var permit = await IssuedElectricalPermitAsync();
        Assert.False((await _service.ScheduleInspectionAsync(permit, InspectionType.RoughElectrical, DateOnly.FromDateTime(Now), _applicant)).Ok);
        Assert.False((await _service.ScheduleInspectionAsync(permit, InspectionType.RoughElectrical, new DateOnly(2026, 10, 31), _applicant)).Ok);
    }

    [Fact]
    public async Task Failing_an_inspection_requires_notes_and_only_assigned_inspector_can_record()
    {
        var permit = await IssuedElectricalPermitAsync();
        await _service.ScheduleInspectionAsync(permit, InspectionType.RoughElectrical, Tomorrow, _applicant);
        var inspection = permit.Inspections.Single();
        var other = inspection.InspectorId == _inspectorA.Id ? _inspectorB : _inspectorA;

        Assert.False((await _service.RecordResultAsync(inspection, InspectionResult.Passed, null, null, null, other)).Ok);
        Assert.False((await _service.RecordResultAsync(inspection, InspectionResult.Failed, "  ", null, null, Assigned(inspection))).Ok);
        Assert.True((await _service.RecordResultAsync(inspection, InspectionResult.Failed, "No GFCI", null, null, Assigned(inspection))).Ok);
        Assert.Equal([InspectionType.RoughElectrical], InspectionRules.Schedulable(permit));
    }

    [Fact]
    public async Task Requesting_corrections_requires_a_comment()
    {
        var permit = await _service.CreateDraftAsync(
            new PermitInput(PermitType.Demolition, "9 Elm St", null, "Remove shed", 0, null, null), _applicant);
        await _service.ApplyActionAsync(permit, PermitAction.Submit, _applicant, null);
        await _service.ApplyActionAsync(permit, PermitAction.StartReview, _clerk, null);

        var result = await _service.ApplyActionAsync(permit, PermitAction.RequestCorrections, _clerk, "");
        Assert.False(result.Ok);
        Assert.Equal(PermitStatus.UnderReview, permit.Status);
    }

    private CurrentUser Assigned(Inspection i) => i.InspectorId == _inspectorA.Id ? _inspectorA : _inspectorB;

    private sealed class FixedClock(DateTime local) : TimeProvider
    {
        public override TimeZoneInfo LocalTimeZone => TimeZoneInfo.Utc;
        public override DateTimeOffset GetUtcNow() => new(local, TimeSpan.Zero);
    }

    private sealed class FakeEnvironment : IWebHostEnvironment
    {
        public string WebRootPath { get; set; } = Path.GetTempPath();
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ApplicationName { get; set; } = "Tests";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = Path.Combine(Path.GetTempPath(), "permittrack-tests");
        public string EnvironmentName { get; set; } = "Test";
    }
}
