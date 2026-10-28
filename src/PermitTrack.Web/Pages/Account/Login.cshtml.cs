using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using PermitTrack.Web.Data;
using PermitTrack.Web.Domain;

namespace PermitTrack.Web.Pages.Account;

/// <summary>
/// Demo sign-in: pick a seeded user to see the app from that role. A real deployment would swap this
/// for ASP.NET Core Identity or Entra ID; everything downstream only depends on the role claims.
/// </summary>
public class LoginModel(AppDbContext db) : PageModel
{
    public List<AppUser> Users { get; private set; } = [];

    [BindProperty(SupportsGet = true)] public string? ReturnUrl { get; set; }

    public async Task OnGetAsync() =>
        Users = (await db.Users.ToListAsync()).OrderBy(u => u.Role).ThenBy(u => u.Name).ToList();

    public async Task<IActionResult> OnPostAsync(int userId)
    {
        var user = await db.Users.FindAsync(userId);
        if (user is null)
            return RedirectToPage();

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Name, user.Name),
            new Claim(ClaimTypes.Role, user.Role.ToString())
        ], CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(new ClaimsPrincipal(identity));

        return Url.IsLocalUrl(ReturnUrl) ? LocalRedirect(ReturnUrl) : RedirectToPage("/Index");
    }

    public async Task<IActionResult> OnPostLogoutAsync()
    {
        await HttpContext.SignOutAsync();
        return RedirectToPage();
    }
}
