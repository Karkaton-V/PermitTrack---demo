using PermitTrack.Web.Domain;

namespace PermitTrack.Tests;

public class WidgetCatalogTests
{
    [Fact]
    public void Defaults_only_include_widgets_for_the_role()
    {
        var applicant = WidgetCatalog.Defaults(UserRole.Applicant).Select(w => w.Id).ToList();
        Assert.Contains("my-permits", applicant);
        Assert.DoesNotContain("review-queue", applicant);
        Assert.DoesNotContain("kpis", WidgetCatalog.Defaults(UserRole.Inspector).Select(w => w.Id));
    }

    [Fact]
    public void Saved_order_and_choices_are_kept()
    {
        var saved = new[]
        {
            new WidgetPref("expiring", true, WidgetSize.Wide),
            new WidgetPref("kpis", false, WidgetSize.Normal),
        };
        var result = WidgetCatalog.Normalize(UserRole.Clerk, saved);
        Assert.Equal(saved, result.Take(2));
    }

    [Fact]
    public void Widgets_added_later_are_appended_and_unknown_or_forbidden_ones_dropped()
    {
        var saved = new[]
        {
            new WidgetPref("my-permits", true, WidgetSize.Wide),    // applicant-only: not allowed for clerks
            new WidgetPref("no-such-widget", true, WidgetSize.Normal),
            new WidgetPref("review-queue", true, WidgetSize.Normal),
            new WidgetPref("review-queue", false, WidgetSize.Wide),  // duplicate
        };
        var result = WidgetCatalog.Normalize(UserRole.Clerk, saved);

        Assert.Equal("review-queue", result[0].Id);
        Assert.True(result[0].Visible);
        Assert.DoesNotContain(result, w => w.Id is "my-permits" or "no-such-widget");
        Assert.Equal(WidgetCatalog.Defaults(UserRole.Clerk).Select(w => w.Id).Order(), result.Select(w => w.Id).Order());
    }

    [Fact]
    public void Settings_round_trip_through_json()
    {
        var settings = new UserSettings();
        var layout = WidgetCatalog.Defaults(UserRole.Inspector).Reverse().Select(w => w with { Visible = false }).ToList();
        settings.SetWidgets(UserRole.Inspector, layout);
        Assert.Equal(layout, settings.GetWidgets(UserRole.Inspector));
    }
}
