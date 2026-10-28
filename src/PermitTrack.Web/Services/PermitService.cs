using Microsoft.EntityFrameworkCore;
using PermitTrack.Web.Data;
using PermitTrack.Web.Domain;

namespace PermitTrack.Web.Services;

public record ServiceResult(bool Ok, string? Error = null)
{
    public static readonly ServiceResult Success = new(true);
    public static ServiceResult Fail(string error) => new(false, error);
}

public record PermitInput(
    PermitType Type,
    string Address,
    string? ParcelNumber,
    string Description,
    decimal Valuation,
    string? ContractorName,
    string? ContractorLicense);

/// <summary>Every state change to a permit goes through here so rules and the audit trail are applied consistently.</summary>
public class PermitService(AppDbContext db, TimeProvider clock, IWebHostEnvironment env)
{
    public const long MaxUploadBytes = 10 * 1024 * 1024;
    public static readonly string[] AllowedExtensions = [".pdf", ".png", ".jpg", ".jpeg"];

    private DateTime Now => clock.GetLocalNow().DateTime;
    public string UploadDirectory => Path.Combine(env.ContentRootPath, "App_Data", "uploads");

    public Task<Permit?> LoadAsync(int id) =>
        db.Permits
            .Include(p => p.Applicant)
            .Include(p => p.Reviewer)
            .Include(p => p.Inspections).ThenInclude(i => i.Inspector)
            .Include(p => p.Documents).ThenInclude(d => d.UploadedBy)
            .Include(p => p.Events).ThenInclude(e => e.User)
            .AsSplitQuery()
            .FirstOrDefaultAsync(p => p.Id == id);

    public async Task<Permit> CreateDraftAsync(PermitInput input, CurrentUser user)
    {
        var permit = new Permit
        {
            Address = input.Address,
            Description = input.Description,
            ApplicantId = user.Id,
            CreatedAt = Now,
            Number = await NextNumberAsync(input.Type)
        };
        Apply(permit, input);
        permit.Events.Add(Event(user, "Application started"));
        db.Permits.Add(permit);
        await db.SaveChangesAsync();
        return permit;
    }

    public async Task<ServiceResult> UpdateDraftAsync(Permit permit, PermitInput input, CurrentUser user)
    {
        if (permit.ApplicantId != user.Id)
            return ServiceResult.Fail("Only the applicant can edit this application.");
        if (!PermitWorkflow.IsEditable(permit.Status))
            return ServiceResult.Fail("This application can no longer be edited.");
        if (input.Type != permit.Type)
            return ServiceResult.Fail("Permit type cannot be changed after the application is started.");

        var oldFee = permit.FeeTotal;
        Apply(permit, input);
        permit.Events.Add(Event(user, "Application edited",
            oldFee != permit.FeeTotal ? $"Fee recalculated from {oldFee:C} to {permit.FeeTotal:C}" : null));
        await db.SaveChangesAsync();
        return ServiceResult.Success;
    }

    public async Task<ServiceResult> ApplyActionAsync(Permit permit, PermitAction action, CurrentUser user, string? note)
    {
        var check = PermitWorkflow.Evaluate(permit, action, user.Role, user.Id);
        if (!check.Allowed)
            return ServiceResult.Fail(check.Reason!);
        if (action == PermitAction.RequestCorrections && string.IsNullOrWhiteSpace(note))
            return ServiceResult.Fail("Describe the corrections the applicant needs to make.");
        if (action == PermitAction.Reject && string.IsNullOrWhiteSpace(note))
            return ServiceResult.Fail("A reason is required to reject an application.");

        var from = permit.Status;
        permit.Status = check.NewStatus;
        switch (action)
        {
            case PermitAction.Submit or PermitAction.Resubmit:
                permit.SubmittedAt = Now;
                break;
            case PermitAction.StartReview:
                permit.ReviewerId = user.Id;
                break;
            case PermitAction.Approve:
                permit.IssuedAt = Now;
                break;
        }
        permit.Events.Add(Event(user, $"{Ui.Label(from)} → {Ui.Label(permit.Status)}", note));
        await db.SaveChangesAsync();
        return ServiceResult.Success;
    }

    public async Task<ServiceResult> RecordPaymentAsync(Permit permit, string reference, CurrentUser user)
    {
        if (!user.IsStaff)
            return ServiceResult.Fail("Only permit office staff can record payments.");
        if (permit.FeePaid)
            return ServiceResult.Fail("Fees are already paid.");
        if (permit.Status is PermitStatus.Draft || PermitWorkflow.IsClosed(permit.Status))
            return ServiceResult.Fail("Payments can only be recorded on active applications.");
        if (string.IsNullOrWhiteSpace(reference))
            return ServiceResult.Fail("Enter a receipt or check number.");

        permit.FeePaidAt = Now;
        permit.PaymentReference = reference.Trim();
        permit.Events.Add(Event(user, $"Payment of {permit.FeeTotal:C} recorded", $"Reference {permit.PaymentReference}"));
        await db.SaveChangesAsync();
        return ServiceResult.Success;
    }

    public async Task<ServiceResult> UploadDocumentAsync(Permit permit, IFormFile file, CurrentUser user)
    {
        if (!user.CanView(permit) || user.Role == UserRole.Inspector && permit.Status != PermitStatus.Issued)
            return ServiceResult.Fail("You can't add documents to this permit.");
        if (PermitWorkflow.IsClosed(permit.Status))
            return ServiceResult.Fail("This permit is closed.");
        if (file.Length == 0)
            return ServiceResult.Fail("The file is empty.");
        if (file.Length > MaxUploadBytes)
            return ServiceResult.Fail($"Files must be {Ui.FileSize(MaxUploadBytes)} or smaller.");
        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!AllowedExtensions.Contains(ext))
            return ServiceResult.Fail("Only PDF, PNG and JPG files are accepted.");

        // Never trust the client's file name on disk; store under a random name and keep the original for display.
        var storedName = $"{Guid.NewGuid():N}{ext}";
        Directory.CreateDirectory(UploadDirectory);
        await using (var stream = File.Create(Path.Combine(UploadDirectory, storedName)))
            await file.CopyToAsync(stream);

        var displayName = Path.GetFileName(file.FileName);
        permit.Documents.Add(new PermitDocument
        {
            FileName = displayName.Length > 255 ? displayName[..255] : displayName,
            StoredName = storedName,
            ContentType = ext == ".pdf" ? "application/pdf" : ext == ".png" ? "image/png" : "image/jpeg",
            SizeBytes = file.Length,
            UploadedAt = Now,
            UploadedById = user.Id
        });
        permit.Events.Add(Event(user, "Document uploaded", displayName));
        await db.SaveChangesAsync();
        return ServiceResult.Success;
    }

    public async Task<ServiceResult> ScheduleInspectionAsync(Permit permit, InspectionType type, DateOnly date, CurrentUser user)
    {
        if (user.Role == UserRole.Inspector || !user.CanView(permit))
            return ServiceResult.Fail("Inspections are requested by the applicant or permit office.");
        if (!InspectionRules.Schedulable(permit).Contains(type))
            return ServiceResult.Fail($"A {Ui.Label(type).ToLowerInvariant()} inspection can't be scheduled right now.");
        var today = DateOnly.FromDateTime(Now);
        if (date <= today)
            return ServiceResult.Fail("Inspections must be requested at least one day ahead.");
        if (date.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return ServiceResult.Fail("Inspections are performed Monday through Friday.");

        var inspector = await AssignInspectorAsync(date);
        permit.Inspections.Add(new Inspection { Type = type, ScheduledFor = date, InspectorId = inspector?.Id });
        permit.Events.Add(Event(user, $"{Ui.Label(type)} inspection scheduled",
            $"{date:ddd, MMM d}" + (inspector is null ? "" : $" — assigned to {inspector.Name}")));
        await db.SaveChangesAsync();
        return ServiceResult.Success;
    }

    public async Task<ServiceResult> RecordResultAsync(Inspection inspection, InspectionResult result, string? notes,
        double? latitude, double? longitude, CurrentUser user)
    {
        var permit = inspection.Permit!;
        if (user.Role != UserRole.Admin && inspection.InspectorId != user.Id)
            return ServiceResult.Fail("Only the assigned inspector can record this result.");
        if (inspection.Result != InspectionResult.Scheduled)
            return ServiceResult.Fail("A result has already been recorded for this inspection.");
        if (result == InspectionResult.Scheduled)
            return ServiceResult.Fail("Choose pass, fail or cancel.");
        if (result == InspectionResult.Failed && string.IsNullOrWhiteSpace(notes))
            return ServiceResult.Fail("List the violations found so the applicant knows what to fix.");

        inspection.Result = result;
        inspection.Notes = notes?.Trim();
        inspection.CompletedAt = Now;
        inspection.Latitude = latitude;
        inspection.Longitude = longitude;
        permit.Events.Add(Event(user, $"{Ui.Label(inspection.Type)} inspection {Ui.Label(result).ToLowerInvariant()}", inspection.Notes));

        if (result == InspectionResult.Passed && InspectionRules.AllRequiredPassed(permit))
        {
            permit.Status = PermitStatus.Finaled;
            permit.FinaledAt = Now;
            permit.Events.Add(Event(user, "Issued → Finaled", "All required inspections passed. Certificate of completion issued."));
        }
        await db.SaveChangesAsync();
        return ServiceResult.Success;
    }

    /// <summary>Balance the day's workload: pick the inspector with the fewest inspections already on that date.</summary>
    private async Task<AppUser?> AssignInspectorAsync(DateOnly date)
    {
        var inspectors = await db.Users.Where(u => u.Role == UserRole.Inspector).ToListAsync();
        var load = await db.Inspections
            .Where(i => i.ScheduledFor == date && i.Result == InspectionResult.Scheduled && i.InspectorId != null)
            .GroupBy(i => i.InspectorId)
            .Select(g => new { Id = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Id!.Value, x => x.Count);
        return inspectors.OrderBy(i => load.GetValueOrDefault(i.Id)).ThenBy(i => i.Id).FirstOrDefault();
    }

    private async Task<string> NextNumberAsync(PermitType type)
    {
        var prefix = $"{type.ToString()[0]}-{Now.Year}-";
        var count = await db.Permits.CountAsync(p => p.Number.StartsWith(prefix));
        return $"{prefix}{count + 1:D4}";
    }

    private static void Apply(Permit permit, PermitInput input)
    {
        permit.Type = input.Type;
        permit.Address = input.Address.Trim();
        permit.ParcelNumber = Blank(input.ParcelNumber);
        permit.Description = input.Description.Trim();
        permit.Valuation = input.Valuation;
        permit.ContractorName = Blank(input.ContractorName);
        permit.ContractorLicense = Blank(input.ContractorLicense);
        permit.FeeTotal = FeeCalculator.Calculate(input.Type, input.Valuation).Total;
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private PermitEvent Event(CurrentUser user, string action, string? note = null) =>
        new() { At = Now, UserId = user.Id, Action = action, Note = string.IsNullOrWhiteSpace(note) ? null : note.Trim() };
}
