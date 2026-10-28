using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using PermitTrack.Web.Domain;
using PermitTrack.Web.Services;

namespace PermitTrack.Web.Pages.Account;

public class SettingsModel(UserSettingsService settingsService) : PageModel
{
    public class WidgetInput
    {
        public string Id { get; set; } = "";
        public bool Visible { get; set; }
        public WidgetSize Size { get; set; }
    }

    [BindProperty] public ThemeColor Theme { get; set; }
    [BindProperty] public ColorMode Mode { get; set; }
    [BindProperty] public Density Density { get; set; }
    [BindProperty] public int Columns { get; set; }
    [BindProperty] public List<WidgetInput> Widgets { get; set; } = [];

    public async Task OnGetAsync()
    {
        var me = CurrentUser.From(User);
        var s = await settingsService.GetAsync(me.Id);
        (Theme, Mode, Density, Columns) = (s.Theme, s.Mode, s.Density, s.Columns);
        Widgets = s.GetWidgets(me.Role).Select(w => new WidgetInput { Id = w.Id, Visible = w.Visible, Size = w.Size }).ToList();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var me = CurrentUser.From(User);
        var s = await settingsService.GetAsync(me.Id);
        s.Theme = Enum.IsDefined(Theme) ? Theme : ThemeColor.Navy;
        s.Mode = Enum.IsDefined(Mode) ? Mode : ColorMode.System;
        s.Density = Enum.IsDefined(Density) ? Density : Density.Comfortable;
        s.Columns = Math.Clamp(Columns, 1, 3);
        s.SetWidgets(me.Role, Widgets.Select(w => new WidgetPref(w.Id, w.Visible, Enum.IsDefined(w.Size) ? w.Size : WidgetSize.Normal)));
        await settingsService.SaveAsync(s);

        TempData["Flash"] = "Settings saved.";
        return RedirectToPage();
    }

    public async Task<IActionResult> OnPostResetAsync()
    {
        await settingsService.ResetAsync(CurrentUser.From(User).Id);
        TempData["Flash"] = "Settings reset to defaults.";
        return RedirectToPage();
    }
}
