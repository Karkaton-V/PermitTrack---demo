using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PermitTrack.Web.Data;
using PermitTrack.Web.Domain;
using PermitTrack.Web.Services;

namespace PermitTrack.Web.Pages.Inspections;

public class IndexModel(AppDbContext db, TimeProvider clock) : PageModel
{
    /// <summary>Inspectors default to their own route; staff default to everyone's.</summary>
    [BindProperty(SupportsGet = true)] public bool? Mine { get; set; }

    public bool ShowingMine { get; private set; }
    public DateOnly Today { get; private set; }
    public List<IGrouping<DateOnly, Inspection>> Days { get; private set; } = [];
    public List<Inspection> RecentlyCompleted { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var me = CurrentUser.From(User);
        Today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        ShowingMine = Mine ?? me.Role == UserRole.Inspector;

        var query = db.Inspections.Include(i => i.Permit).Include(i => i.Inspector).AsQueryable();
        if (ShowingMine)
            query = query.Where(i => i.InspectorId == me.Id);

        var open = await query.Where(i => i.Result == InspectionResult.Scheduled).OrderBy(i => i.ScheduledFor).ToListAsync();
        Days = open.GroupBy(i => i.ScheduledFor).ToList();

        RecentlyCompleted = await query.Where(i => i.Result != InspectionResult.Scheduled)
            .OrderByDescending(i => i.CompletedAt).Take(10).ToListAsync();
    }
}
