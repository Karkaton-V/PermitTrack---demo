using System.Globalization;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using PermitTrack.Web.Data;
using PermitTrack.Web.Domain;
using PermitTrack.Web.Services;

var culture = CultureInfo.GetCultureInfo("en-US");
CultureInfo.DefaultThreadCurrentCulture = culture;
CultureInfo.DefaultThreadCurrentUICulture = culture;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o =>
    o.UseSqlite(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<PermitService>();
builder.Services.AddScoped<UserSettingsService>();

builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/Account/Login";
        o.AccessDeniedPath = "/Account/Login";
        o.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();

builder.Services.AddRazorPages(o =>
{
    o.Conventions.AuthorizeFolder("/");
    o.Conventions.AllowAnonymousToPage("/Account/Login");
    o.Conventions.AllowAnonymousToPage("/Error");
    o.Conventions.AuthorizeFolder("/Inspections", "Inspections");
});
builder.Services.AddAuthorizationBuilder()
    .AddPolicy("Inspections", p => p.RequireRole(nameof(UserRole.Inspector), nameof(UserRole.Clerk), nameof(UserRole.Admin)));

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "App_Data"));
    db.Database.EnsureCreated();
    if (!SeedData.IsCurrent(db))
    {
        // Demo database from an older version of the app: rebuild it rather than migrating.
        db.Database.EnsureDeleted();
        db.Database.EnsureCreated();
    }
    SeedData.Initialize(db, DateTime.Now, scope.ServiceProvider.GetRequiredService<PermitService>().UploadDirectory);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();
app.MapRazorPages()
   .WithStaticAssets();

// Live fee estimate for the application form; uses the same calculator the server saves with.
app.MapGet("/api/fees/estimate", (PermitType type, decimal valuation) =>
        valuation < 0 ? Results.BadRequest("Valuation cannot be negative.") : Results.Ok(FeeCalculator.Calculate(type, valuation)))
    .RequireAuthorization();

app.Run();
