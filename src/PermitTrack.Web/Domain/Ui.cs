using System.Text.RegularExpressions;

namespace PermitTrack.Web.Domain;

/// <summary>Display helpers shared by the domain messages and the Razor pages.</summary>
public static partial class Ui
{
    /// <summary>"CorrectionsRequired" → "Corrections Required".</summary>
    public static string Label(Enum value) => SplitWords().Replace(value.ToString(), " $1");

    public static string StatusClass(PermitStatus status) => status switch
    {
        PermitStatus.Draft => "status-draft",
        PermitStatus.Submitted => "status-submitted",
        PermitStatus.UnderReview => "status-review",
        PermitStatus.CorrectionsRequired => "status-corrections",
        PermitStatus.Issued => "status-issued",
        PermitStatus.Finaled => "status-finaled",
        _ => "status-closed"
    };

    public static string ResultClass(InspectionResult result) => result switch
    {
        InspectionResult.Passed => "status-issued",
        InspectionResult.Failed => "status-corrections",
        InspectionResult.Cancelled => "status-closed",
        _ => "status-submitted"
    };

    public static string FileSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0.#} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB"
    };

    [GeneratedRegex("(?<=[a-z])([A-Z])")]
    private static partial Regex SplitWords();
}
