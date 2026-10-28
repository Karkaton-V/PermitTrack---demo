namespace PermitTrack.Web.Domain;

public record WorkflowCheck(PermitAction Action, bool Allowed, PermitStatus NewStatus, string? Reason = null);

/// <summary>
/// The permit lifecycle as a state machine: which roles may move a permit from which status, and where it lands.
/// Draft → Submitted → UnderReview → Issued, with a CorrectionsRequired loop back to the applicant.
/// Issued → Finaled happens automatically once every required inspection passes (see <see cref="InspectionRules"/>).
/// </summary>
public static class PermitWorkflow
{
    private record Transition(PermitAction Action, PermitStatus From, PermitStatus To, bool ApplicantOnly);

    private static readonly Transition[] Transitions =
    [
        new(PermitAction.Submit, PermitStatus.Draft, PermitStatus.Submitted, ApplicantOnly: true),
        new(PermitAction.Withdraw, PermitStatus.Draft, PermitStatus.Withdrawn, ApplicantOnly: true),
        new(PermitAction.Withdraw, PermitStatus.Submitted, PermitStatus.Withdrawn, ApplicantOnly: true),
        new(PermitAction.Withdraw, PermitStatus.CorrectionsRequired, PermitStatus.Withdrawn, ApplicantOnly: true),
        new(PermitAction.Resubmit, PermitStatus.CorrectionsRequired, PermitStatus.Submitted, ApplicantOnly: true),
        new(PermitAction.StartReview, PermitStatus.Submitted, PermitStatus.UnderReview, ApplicantOnly: false),
        new(PermitAction.RequestCorrections, PermitStatus.UnderReview, PermitStatus.CorrectionsRequired, ApplicantOnly: false),
        new(PermitAction.Approve, PermitStatus.UnderReview, PermitStatus.Issued, ApplicantOnly: false),
        new(PermitAction.Reject, PermitStatus.UnderReview, PermitStatus.Rejected, ApplicantOnly: false),
    ];

    /// <summary>Statuses in which the applicant may still edit the application.</summary>
    public static bool IsEditable(PermitStatus status) =>
        status is PermitStatus.Draft or PermitStatus.CorrectionsRequired;

    public static bool IsClosed(PermitStatus status) =>
        status is PermitStatus.Finaled or PermitStatus.Rejected or PermitStatus.Withdrawn;

    /// <summary>Actions this user could take on the permit right now, including ones blocked by a precondition (with a reason).</summary>
    public static IReadOnlyList<WorkflowCheck> AvailableActions(Permit permit, UserRole role, int userId) =>
        Transitions
            .Where(t => t.From == permit.Status && RoleMatches(t, permit, role, userId))
            .Select(t => Check(permit, t))
            .ToList();

    public static WorkflowCheck Evaluate(Permit permit, PermitAction action, UserRole role, int userId)
    {
        var transition = Transitions.FirstOrDefault(t => t.Action == action && t.From == permit.Status);
        if (transition is null)
            return new(action, false, permit.Status, $"Cannot {Ui.Label(action).ToLowerInvariant()} a permit that is {Ui.Label(permit.Status).ToLowerInvariant()}.");
        if (!RoleMatches(transition, permit, role, userId))
            return new(action, false, permit.Status, transition.ApplicantOnly
                ? "Only the applicant can do this."
                : "Only permit office staff can do this.");
        return Check(permit, transition);
    }

    private static bool RoleMatches(Transition t, Permit permit, UserRole role, int userId) =>
        t.ApplicantOnly
            ? role == UserRole.Applicant && permit.ApplicantId == userId
            : role is UserRole.Clerk or UserRole.Admin;

    private static WorkflowCheck Check(Permit permit, Transition t)
    {
        if (t.Action == PermitAction.Approve && !permit.FeePaid)
            return new(t.Action, false, t.From, "Fees must be paid before the permit can be issued.");
        if (t.Action is PermitAction.Submit or PermitAction.Resubmit && permit.Documents.Count == 0 && permit.Type != PermitType.Demolition)
            return new(t.Action, false, t.From, "Attach at least one plan or document before submitting.");
        return new(t.Action, true, t.To);
    }
}
