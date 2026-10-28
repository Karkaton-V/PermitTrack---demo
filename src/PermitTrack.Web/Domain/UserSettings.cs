using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace PermitTrack.Web.Domain;

public enum ThemeColor { Navy, Forest, Plum, Slate, Sunset, Teal }

public enum ColorMode { System, Light, Dark }

public enum Density { Comfortable, Compact }

public enum WidgetSize { Normal, Wide }

/// <summary>One widget's place on a user's dashboard.</summary>
public record WidgetPref(string Id, bool Visible, WidgetSize Size);

/// <summary>Per-user appearance and dashboard preferences. One row per user, created on first save.</summary>
public class UserSettings
{
    [Key] public int UserId { get; set; }
    public AppUser? User { get; set; }

    public ThemeColor Theme { get; set; } = ThemeColor.Navy;
    public ColorMode Mode { get; set; } = ColorMode.System;
    public Density Density { get; set; } = Density.Comfortable;

    /// <summary>Number of dashboard columns on wide screens (1–3).</summary>
    [Range(1, 3)] public int Columns { get; set; } = 2;

    /// <summary>Ordered widget list, stored as JSON so new widgets don't need schema changes.</summary>
    [MaxLength(4000)] public string? WidgetsJson { get; set; }

    public IReadOnlyList<WidgetPref> GetWidgets(UserRole role) =>
        WidgetCatalog.Normalize(role, WidgetsJson is null ? null : JsonSerializer.Deserialize<List<WidgetPref>>(WidgetsJson));

    public void SetWidgets(UserRole role, IEnumerable<WidgetPref> widgets) =>
        WidgetsJson = JsonSerializer.Serialize(WidgetCatalog.Normalize(role, widgets));
}

public record WidgetDefinition(string Id, string Title, string Description, UserRole[] Roles, WidgetSize DefaultSize, bool DefaultVisible = true);

/// <summary>Every dashboard widget, which roles can use it, and the default layout.</summary>
public static class WidgetCatalog
{
    private static readonly UserRole[] Staff = [UserRole.Clerk, UserRole.Admin];
    private static readonly UserRole[] Field = [UserRole.Clerk, UserRole.Admin, UserRole.Inspector];
    private static readonly UserRole[] Everyone = Enum.GetValues<UserRole>();

    public static readonly IReadOnlyList<WidgetDefinition> All =
    [
        new("kpis", "Key numbers", "Reviews waiting, overdue, active permits, today's inspections, fees collected.", Staff, WidgetSize.Wide),
        new("attention", "Needs your attention", "Drafts to finish, corrections requested, failed inspections, permits about to expire.", [UserRole.Applicant], WidgetSize.Wide),
        new("review-queue", "Review queue", "Applications waiting for staff review, oldest first.", Staff, WidgetSize.Normal),
        new("expiring", "Expiring soon", "Issued permits that lapse within 30 days.", Staff, WidgetSize.Normal),
        new("inspections-today", "Inspections today", "Today's inspections (inspectors see their own route).", Field, WidgetSize.Normal),
        new("inspections-upcoming", "Coming up", "Upcoming scheduled inspections.", Field, WidgetSize.Normal),
        new("my-permits", "My permits", "Every permit you've applied for.", [UserRole.Applicant], WidgetSize.Wide),
        new("status-breakdown", "Permits by status", "Bar chart of how many permits are in each stage.", Staff, WidgetSize.Normal),
        new("recent-activity", "Recent activity", "Latest changes across permits you can see.", Everyone, WidgetSize.Normal, DefaultVisible: false),
    ];

    public static WidgetDefinition? Find(string id) => All.FirstOrDefault(w => w.Id == id);

    public static IReadOnlyList<WidgetPref> Defaults(UserRole role) =>
        All.Where(w => w.Roles.Contains(role)).Select(w => new WidgetPref(w.Id, w.DefaultVisible, w.DefaultSize)).ToList();

    /// <summary>
    /// Keep the user's order and choices, drop widgets their role can't use (or that no longer exist),
    /// and append any widgets added since they last saved.
    /// </summary>
    public static IReadOnlyList<WidgetPref> Normalize(UserRole role, IEnumerable<WidgetPref>? saved)
    {
        var allowed = Defaults(role);
        if (saved is null)
            return allowed;
        var result = saved
            .Where(s => allowed.Any(a => a.Id == s.Id))
            .DistinctBy(s => s.Id)
            .ToList();
        result.AddRange(allowed.Where(a => result.All(r => r.Id != a.Id)));
        return result;
    }
}
