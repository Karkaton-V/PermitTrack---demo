namespace PermitTrack.Web.Domain;

/// <summary>Which inspections each permit type needs, in what order, and when a permit lapses.</summary>
public static class InspectionRules
{
    /// <summary>Days an issued permit stays valid without a passed inspection (the common IRC/IBC 180-day rule).</summary>
    public const int DaysUntilExpiration = 180;

    /// <summary>Days a submitted application may wait before staff review counts as overdue.</summary>
    public const int ReviewTargetDays = 7;

    private static readonly Dictionary<PermitType, InspectionType[]> Required = new()
    {
        [PermitType.Building] = [InspectionType.Footing, InspectionType.Framing, InspectionType.Insulation, InspectionType.FinalBuilding],
        [PermitType.Electrical] = [InspectionType.RoughElectrical, InspectionType.FinalElectrical],
        [PermitType.Plumbing] = [InspectionType.RoughPlumbing, InspectionType.FinalPlumbing],
        [PermitType.Mechanical] = [InspectionType.RoughMechanical, InspectionType.FinalMechanical],
        [PermitType.Demolition] = [InspectionType.SiteClearance],
    };

    public static IReadOnlyList<InspectionType> RequiredFor(PermitType type) => Required[type];

    public static bool HasPassed(Permit permit, InspectionType type) =>
        permit.Inspections.Any(i => i.Type == type && i.Result == InspectionResult.Passed);

    /// <summary>
    /// Inspections that may be scheduled now: the permit must be issued, the inspection not already passed or pending,
    /// and every earlier inspection in the sequence passed (no framing before the footing is approved).
    /// </summary>
    public static IReadOnlyList<InspectionType> Schedulable(Permit permit)
    {
        if (permit.Status != PermitStatus.Issued)
            return [];

        var result = new List<InspectionType>();
        foreach (var type in RequiredFor(permit.Type))
        {
            if (HasPassed(permit, type))
                continue;
            var pending = permit.Inspections.Any(i => i.Type == type && i.Result == InspectionResult.Scheduled);
            if (!pending)
                result.Add(type);
            break; // later inspections wait on this one
        }
        return result;
    }

    public static bool AllRequiredPassed(Permit permit) =>
        RequiredFor(permit.Type).All(t => HasPassed(permit, t));

    /// <summary>Issued permits expire 180 days after issuance or after the most recent passed inspection.</summary>
    public static DateTime? ExpiresOn(Permit permit)
    {
        if (permit.Status != PermitStatus.Issued || permit.IssuedAt is null)
            return null;
        var lastActivity = permit.Inspections
            .Where(i => i.Result == InspectionResult.Passed && i.CompletedAt is not null)
            .Select(i => i.CompletedAt!.Value)
            .Append(permit.IssuedAt.Value)
            .Max();
        return lastActivity.AddDays(DaysUntilExpiration);
    }

    public static bool IsReviewOverdue(Permit permit, DateTime now) =>
        permit.Status == PermitStatus.Submitted
        && permit.SubmittedAt is not null
        && (now - permit.SubmittedAt.Value).TotalDays > ReviewTargetDays;
}
