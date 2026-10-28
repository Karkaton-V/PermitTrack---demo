using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PermitTrack.Web.Domain;
using PermitTrack.Web.Services;

namespace PermitTrack.Web.Pages.Permits;

public class DetailsModel(PermitService permits, TimeProvider clock) : PageModel
{
    public Permit Permit { get; private set; } = null!;
    public CurrentUser Me { get; private set; } = null!;
    public IReadOnlyList<WorkflowCheck> Actions { get; private set; } = [];
    public IReadOnlyList<InspectionType> Schedulable { get; private set; } = [];
    public FeeBreakdown Fee { get; private set; } = null!;
    public DateTime Now => clock.GetLocalNow().DateTime;

    public bool CanEdit => Me.Id == Permit.ApplicantId && PermitWorkflow.IsEditable(Permit.Status);
    public bool CanUpload => !PermitWorkflow.IsClosed(Permit.Status)
        && (Me.Role != UserRole.Inspector || Permit.Status == PermitStatus.Issued);
    public bool CanSchedule => Me.Role != UserRole.Inspector && Schedulable.Count > 0;
    public bool CanRecordPayment => Me.IsStaff && !Permit.FeePaid
        && Permit.Status != PermitStatus.Draft && !PermitWorkflow.IsClosed(Permit.Status);

    /// <summary>First weekday after today: the earliest date an inspection can be requested for.</summary>
    public DateOnly EarliestInspectionDate
    {
        get
        {
            var d = DateOnly.FromDateTime(Now).AddDays(1);
            while (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) d = d.AddDays(1);
            return d;
        }
    }

    public async Task<IActionResult> OnGetAsync(int id) => await LoadAsync(id) ?? Page();

    public async Task<IActionResult> OnGetDocumentAsync(int id, int docId)
    {
        if (await LoadAsync(id) is { } denied)
            return denied;
        var doc = Permit.Documents.FirstOrDefault(d => d.Id == docId);
        if (doc is null)
            return NotFound();
        var path = Path.Combine(permits.UploadDirectory, doc.StoredName);
        return System.IO.File.Exists(path) ? PhysicalFile(path, doc.ContentType, doc.FileName) : NotFound();
    }

    public Task<IActionResult> OnPostActionAsync(int id, PermitAction action, string? note) =>
        Run(id, () => permits.ApplyActionAsync(Permit, action, Me, note), action switch
        {
            PermitAction.Submit or PermitAction.Resubmit => "Application submitted. The permit office will review it.",
            PermitAction.StartReview => "You're now the reviewer on this application.",
            PermitAction.RequestCorrections => "Sent back to the applicant for corrections.",
            PermitAction.Approve => "Permit issued. The applicant can now request inspections.",
            PermitAction.Reject => "Application rejected.",
            _ => "Application withdrawn."
        });

    public Task<IActionResult> OnPostPayAsync(int id, string reference) =>
        Run(id, () => permits.RecordPaymentAsync(Permit, reference, Me), "Payment recorded.");

    public Task<IActionResult> OnPostUploadAsync(int id, IFormFile? file) =>
        Run(id, () => file is null
            ? Task.FromResult(ServiceResult.Fail("Choose a file to upload."))
            : permits.UploadDocumentAsync(Permit, file, Me), "Document uploaded.");

    public Task<IActionResult> OnPostScheduleAsync(int id, InspectionType type, DateOnly date) =>
        Run(id, () => permits.ScheduleInspectionAsync(Permit, type, date, Me), "Inspection scheduled.");

    /// <summary>Load, run a service operation, flash the outcome and redirect back (post/redirect/get).</summary>
    private async Task<IActionResult> Run(int id, Func<Task<ServiceResult>> operation, string success)
    {
        if (await LoadAsync(id) is { } denied)
            return denied;
        var result = await operation();
        TempData[result.Ok ? "Flash" : "Error"] = result.Ok ? success : result.Error;
        return RedirectToPage(new { id });
    }

    /// <summary>Returns a result to short-circuit with when the permit is missing or not visible to this user.</summary>
    private async Task<IActionResult?> LoadAsync(int id)
    {
        Me = CurrentUser.From(User);
        var permit = await permits.LoadAsync(id);
        // Applicants get a 404 rather than 403 for other people's permits so permit IDs can't be probed.
        if (permit is null || !Me.CanView(permit))
            return NotFound();

        Permit = permit;
        Actions = PermitWorkflow.AvailableActions(permit, Me.Role, Me.Id);
        Schedulable = InspectionRules.Schedulable(permit);
        Fee = FeeCalculator.Calculate(permit.Type, permit.Valuation);
        return null;
    }
}
