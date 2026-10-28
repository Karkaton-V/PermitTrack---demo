using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PermitTrack.Web.Data;
using PermitTrack.Web.Domain;
using PermitTrack.Web.Services;

namespace PermitTrack.Web.Pages;

public record AttentionItem(Permit Permit, string Message);

public class IndexModel(AppDbContext db, TimeProvider clock, UserSettingsService settingsService) : PageModel
{
    public CurrentUser Me { get; private set; } = null!;
    public DateTime Now { get; private set; }
    public DateOnly Today => DateOnly.FromDateTime(Now);

    // Layout
    public IReadOnlyList<WidgetPref> Widgets { get; private set; } = [];
    public int Columns { get; private set; } = 2;

    // Staff
    public List<Permit> ReviewQueue { get; private set; } = [];
    public List<Permit> ExpiringSoon { get; private set; } = [];
    public int OverdueCount { get; private set; }
    public int IssuedCount { get; private set; }
    public decimal FeesCollected30Days { get; private set; }
    public Dictionary<PermitStatus, int> StatusCounts { get; private set; } = [];

    // Inspectors and staff
    public List<Inspection> TodaysInspections { get; private set; } = [];
    public List<Inspection> UpcomingInspections { get; private set; } = [];

    // Applicants
    public List<Permit> MyPermits { get; private set; } = [];
    public List<AttentionItem> NeedsAttention { get; private set; } = [];

    // Everyone
    public List<PermitEvent> RecentActivity { get; private set; } = [];

    public async Task OnGetAsync()
    {
        Me = CurrentUser.From(User);
        Now = clock.GetLocalNow().DateTime;

        var settings = await settingsService.GetAsync(Me.Id);
        Widgets = settings.GetWidgets(Me.Role).Where(w => w.Visible).ToList();
        Columns = Math.Clamp(settings.Columns, 1, 3);

        var events = db.Events.Include(e => e.Permit).Include(e => e.User).AsQueryable();
        if (Me.Role == UserRole.Applicant)
            events = events.Where(e => e.Permit!.ApplicantId == Me.Id);
        RecentActivity = await events.OrderByDescending(e => e.At).Take(8).ToListAsync();

        var permits = db.Permits.Include(p => p.Applicant).Include(p => p.Inspections).AsSplitQuery();

        if (Me.Role == UserRole.Applicant)
        {
            MyPermits = await permits.Where(p => p.ApplicantId == Me.Id).OrderByDescending(p => p.CreatedAt).ToListAsync();
            NeedsAttention = MyPermits.SelectMany(Attention).ToList();
            return;
        }

        var inspections = db.Inspections.Include(i => i.Permit).Include(i => i.Inspector)
            .Where(i => i.Result == InspectionResult.Scheduled);
        if (Me.Role == UserRole.Inspector)
            inspections = inspections.Where(i => i.InspectorId == Me.Id);
        var open = await inspections.Where(i => i.ScheduledFor >= Today).OrderBy(i => i.ScheduledFor).ToListAsync();
        TodaysInspections = open.Where(i => i.ScheduledFor == Today).ToList();
        UpcomingInspections = open.Where(i => i.ScheduledFor > Today).Take(8).ToList();

        if (Me.Role == UserRole.Inspector)
            return;

        // The dataset is small enough to aggregate in memory (SQLite can't sum decimals server-side).
        var all = await permits.ToListAsync();
        StatusCounts = all.GroupBy(p => p.Status).ToDictionary(g => g.Key, g => g.Count());
        ReviewQueue = all.Where(p => p.Status is PermitStatus.Submitted or PermitStatus.UnderReview)
            .OrderBy(p => p.SubmittedAt).ToList();
        OverdueCount = all.Count(p => InspectionRules.IsReviewOverdue(p, Now));
        IssuedCount = StatusCounts.GetValueOrDefault(PermitStatus.Issued);
        ExpiringSoon = all.Where(p => InspectionRules.ExpiresOn(p) is { } e && e <= Now.AddDays(30))
            .OrderBy(p => InspectionRules.ExpiresOn(p)).ToList();
        FeesCollected30Days = all.Where(p => p.FeePaidAt >= Now.AddDays(-30)).Sum(p => p.FeeTotal);
    }

    /// <summary>The × button on a widget: hides it without a trip to the settings page.</summary>
    public async Task<IActionResult> OnPostHideAsync(string widget)
    {
        var me = CurrentUser.From(User);
        if (WidgetCatalog.Find(widget) is { } def)
        {
            await settingsService.SetWidgetVisibilityAsync(me.Id, me.Role, widget, false);
            TempData["Flash"] = $"Hid \"{def.Title}\". You can bring it back in Settings.";
        }
        return RedirectToPage();
    }

    private IEnumerable<AttentionItem> Attention(Permit p)
    {
        switch (p.Status)
        {
            case PermitStatus.Draft:
                yield return new(p, "Not submitted yet — finish the application and submit it for review.");
                break;
            case PermitStatus.CorrectionsRequired:
                yield return new(p, "The permit office requested corrections.");
                break;
            case PermitStatus.Issued:
                var failed = p.Inspections
                    .Where(i => i.Result == InspectionResult.Failed && !InspectionRules.HasPassed(p, i.Type)
                        && !p.Inspections.Any(o => o.Type == i.Type && o.Result == InspectionResult.Scheduled))
                    .Select(i => Ui.Label(i.Type)).Distinct().ToList();
                if (failed.Count > 0)
                    yield return new(p, $"{string.Join(", ", failed)} inspection failed — fix the items noted and request a re-inspection.");
                if (InspectionRules.ExpiresOn(p) is { } expires && expires <= Now.AddDays(30))
                    yield return new(p, $"Permit expires {expires:MMM d} unless an inspection passes before then.");
                break;
        }
    }
}
