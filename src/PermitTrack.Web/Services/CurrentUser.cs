using System.Security.Claims;
using PermitTrack.Web.Domain;

namespace PermitTrack.Web.Services;

public record CurrentUser(int Id, string Name, UserRole Role)
{
    public bool IsStaff => Role is UserRole.Clerk or UserRole.Admin;

    public static CurrentUser From(ClaimsPrincipal principal) => new(
        int.Parse(principal.FindFirstValue(ClaimTypes.NameIdentifier)!),
        principal.Identity?.Name ?? "",
        Enum.Parse<UserRole>(principal.FindFirstValue(ClaimTypes.Role)!));

    /// <summary>Applicants see only their own permits; staff and inspectors see everything.</summary>
    public bool CanView(Permit permit) => Role != UserRole.Applicant || permit.ApplicantId == Id;
}
