using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PermitTrack.Web.Data;
using PermitTrack.Web.Domain;
using PermitTrack.Web.Services;

namespace PermitTrack.Web.Pages.Permits;

public class IndexModel(AppDbContext db) : PageModel
{
    [BindProperty(SupportsGet = true)] public string? Q { get; set; }
    [BindProperty(SupportsGet = true)] public PermitStatus? Status { get; set; }
    [BindProperty(SupportsGet = true)] public PermitType? Type { get; set; }

    public List<Permit> Permits { get; private set; } = [];

    public async Task OnGetAsync() => Permits = await QueryAsync();

    public async Task<IActionResult> OnGetExportAsync()
    {
        var rows = await QueryAsync();
        var csv = new StringBuilder("Number,Type,Status,Address,Applicant,Valuation,Fee,FeePaid,Submitted,Issued,Finaled\n");
        foreach (var p in rows)
        {
            csv.AppendJoin(',', p.Number, p.Type, p.Status, Csv(p.Address), Csv(p.Applicant!.Name), p.Valuation, p.FeeTotal,
                p.FeePaid ? "yes" : "no", p.SubmittedAt?.ToString("yyyy-MM-dd"), p.IssuedAt?.ToString("yyyy-MM-dd"), p.FinaledAt?.ToString("yyyy-MM-dd"));
            csv.Append('\n');
        }
        return File(Encoding.UTF8.GetBytes(csv.ToString()), "text/csv", $"permits-{DateTime.Today:yyyy-MM-dd}.csv");
    }

    private async Task<List<Permit>> QueryAsync()
    {
        var me = CurrentUser.From(User);
        var query = db.Permits.Include(p => p.Applicant).AsQueryable();
        if (me.Role == UserRole.Applicant)
            query = query.Where(p => p.ApplicantId == me.Id);
        if (Status is not null)
            query = query.Where(p => p.Status == Status);
        if (Type is not null)
            query = query.Where(p => p.Type == Type);
        if (!string.IsNullOrWhiteSpace(Q))
        {
            var term = Q.Trim();
            query = query.Where(p => EF.Functions.Like(p.Number, $"%{term}%")
                || EF.Functions.Like(p.Address, $"%{term}%")
                || EF.Functions.Like(p.Description, $"%{term}%")
                || (p.ContractorName != null && EF.Functions.Like(p.ContractorName, $"%{term}%"))
                || EF.Functions.Like(p.Applicant!.Name, $"%{term}%"));
        }
        return await query.OrderByDescending(p => p.CreatedAt).ToListAsync();
    }

    /// <summary>Quote a CSV field and neutralize spreadsheet formula injection.</summary>
    private static string Csv(string value)
    {
        if (value.Length > 0 && "=+-@".Contains(value[0]))
            value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
