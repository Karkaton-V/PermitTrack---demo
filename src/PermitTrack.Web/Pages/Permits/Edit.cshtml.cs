using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PermitTrack.Web.Domain;
using PermitTrack.Web.Services;

namespace PermitTrack.Web.Pages.Permits;

public class EditModel(PermitService permits) : PageModel
{
    public class FormInput
    {
        public PermitType Type { get; set; } = PermitType.Building;

        [Required, StringLength(200, MinimumLength = 5), Display(Name = "Project address")]
        public string Address { get; set; } = "";

        [StringLength(30), Display(Name = "Parcel number")]
        public string? ParcelNumber { get; set; }

        [Required, StringLength(2000, MinimumLength = 10), Display(Name = "Scope of work")]
        public string Description { get; set; } = "";

        [Range(0, 100_000_000), Display(Name = "Estimated project valuation")]
        public decimal Valuation { get; set; }

        [StringLength(100), Display(Name = "Contractor")]
        public string? ContractorName { get; set; }

        [StringLength(30), Display(Name = "Contractor license #")]
        public string? ContractorLicense { get; set; }

        public PermitInput ToInput() => new(Type, Address, ParcelNumber, Description, Valuation, ContractorName, ContractorLicense);
    }

    [BindProperty] public FormInput Input { get; set; } = new();
    public Permit? Existing { get; private set; }
    public FeeBreakdown Fee => FeeCalculator.Calculate(Input.Type, Math.Max(0, Input.Valuation));

    public async Task<IActionResult> OnGetAsync(int? id)
    {
        var me = CurrentUser.From(User);
        if (me.Role != UserRole.Applicant)
            return Forbid();
        if (id is null)
            return Page();

        Existing = await permits.LoadAsync(id.Value);
        if (Existing is null || Existing.ApplicantId != me.Id)
            return NotFound();
        if (!PermitWorkflow.IsEditable(Existing.Status))
            return RedirectToPage("Details", new { id });

        Input = new FormInput
        {
            Type = Existing.Type,
            Address = Existing.Address,
            ParcelNumber = Existing.ParcelNumber,
            Description = Existing.Description,
            Valuation = Existing.Valuation,
            ContractorName = Existing.ContractorName,
            ContractorLicense = Existing.ContractorLicense
        };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id)
    {
        var me = CurrentUser.From(User);
        if (me.Role != UserRole.Applicant)
            return Forbid();
        if (id is not null)
        {
            Existing = await permits.LoadAsync(id.Value);
            if (Existing is null || Existing.ApplicantId != me.Id)
                return NotFound();
            Input.Type = Existing.Type;
        }
        if (Input.Type != PermitType.Demolition && Input.Valuation <= 0)
            ModelState.AddModelError("Input.Valuation", "Enter the estimated cost of the work.");
        if (!ModelState.IsValid)
            return Page();

        if (Existing is null)
        {
            var created = await permits.CreateDraftAsync(Input.ToInput(), me);
            TempData["Flash"] = $"Application {created.Number} saved as a draft. Attach your plans, then submit it for review.";
            return RedirectToPage("Details", new { id = created.Id });
        }

        var result = await permits.UpdateDraftAsync(Existing, Input.ToInput(), me);
        if (!result.Ok)
        {
            ModelState.AddModelError("", result.Error!);
            return Page();
        }
        TempData["Flash"] = "Application updated.";
        return RedirectToPage("Details", new { id });
    }
}
