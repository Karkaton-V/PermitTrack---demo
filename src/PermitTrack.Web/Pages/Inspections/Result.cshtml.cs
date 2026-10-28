using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PermitTrack.Web.Data;
using PermitTrack.Web.Domain;
using PermitTrack.Web.Services;

namespace PermitTrack.Web.Pages.Inspections;

/// <summary>Field form for recording an inspection result; designed to be used on a phone at the job site.</summary>
public class ResultModel(AppDbContext db, PermitService permits) : PageModel
{
    public Inspection Inspection { get; private set; } = null!;
    public CurrentUser Me { get; private set; } = null!;
    public bool CanRecord => Inspection.Result == InspectionResult.Scheduled
        && (Inspection.InspectorId == Me.Id || Me.Role == UserRole.Admin);

    [BindProperty] public InspectionResult Outcome { get; set; } = InspectionResult.Passed;
    [BindProperty] public string? Notes { get; set; }
    [BindProperty] public double? Latitude { get; set; }
    [BindProperty] public double? Longitude { get; set; }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ? Page() : NotFound();

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!await LoadAsync(id))
            return NotFound();

        var result = await permits.RecordResultAsync(Inspection, Outcome, Notes, Latitude, Longitude, Me);
        if (!result.Ok)
        {
            ModelState.AddModelError("", result.Error!);
            return Page();
        }

        TempData["Flash"] = Inspection.Permit!.Status == PermitStatus.Finaled
            ? $"{Ui.Label(Inspection.Type)} recorded. All inspections passed — {Inspection.Permit.Number} is finaled."
            : $"{Ui.Label(Inspection.Type)} inspection recorded as {Ui.Label(Outcome).ToLowerInvariant()}.";
        return RedirectToPage("Index");
    }

    private async Task<bool> LoadAsync(int id)
    {
        Me = CurrentUser.From(User);
        // Load the whole permit (with its other inspections) so finaling rules can see every result.
        var inspection = await db.Inspections.Include(i => i.Inspector).FirstOrDefaultAsync(i => i.Id == id);
        if (inspection is null)
            return false;
        await permits.LoadAsync(inspection.PermitId);
        Inspection = inspection;
        return true;
    }
}
