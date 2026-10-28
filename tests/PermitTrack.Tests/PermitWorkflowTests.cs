using PermitTrack.Web.Domain;

namespace PermitTrack.Tests;

public class PermitWorkflowTests
{
    private const int ApplicantId = 1;
    private const int OtherApplicantId = 2;
    private const int ClerkId = 3;

    private static Permit NewPermit(PermitStatus status, bool feePaid = false, bool withDocument = true)
    {
        var p = new Permit { Address = "1 Main St", Description = "Test", ApplicantId = ApplicantId, Status = status, Type = PermitType.Building };
        if (feePaid) p.FeePaidAt = DateTime.Now;
        if (withDocument) p.Documents.Add(new PermitDocument { FileName = "plan.pdf", StoredName = "x.pdf", ContentType = "application/pdf" });
        return p;
    }

    [Theory]
    [InlineData(PermitStatus.Draft, PermitAction.Submit, UserRole.Applicant, ApplicantId, PermitStatus.Submitted)]
    [InlineData(PermitStatus.Submitted, PermitAction.StartReview, UserRole.Clerk, ClerkId, PermitStatus.UnderReview)]
    [InlineData(PermitStatus.UnderReview, PermitAction.RequestCorrections, UserRole.Clerk, ClerkId, PermitStatus.CorrectionsRequired)]
    [InlineData(PermitStatus.CorrectionsRequired, PermitAction.Resubmit, UserRole.Applicant, ApplicantId, PermitStatus.Submitted)]
    [InlineData(PermitStatus.UnderReview, PermitAction.Reject, UserRole.Admin, ClerkId, PermitStatus.Rejected)]
    [InlineData(PermitStatus.Submitted, PermitAction.Withdraw, UserRole.Applicant, ApplicantId, PermitStatus.Withdrawn)]
    public void Valid_transitions_are_allowed(PermitStatus from, PermitAction action, UserRole role, int userId, PermitStatus expected)
    {
        var check = PermitWorkflow.Evaluate(NewPermit(from), action, role, userId);
        Assert.True(check.Allowed, check.Reason);
        Assert.Equal(expected, check.NewStatus);
    }

    [Fact]
    public void Clerk_cannot_submit_on_behalf_of_applicant() =>
        Assert.False(PermitWorkflow.Evaluate(NewPermit(PermitStatus.Draft), PermitAction.Submit, UserRole.Clerk, ClerkId).Allowed);

    [Fact]
    public void Applicant_cannot_act_on_someone_elses_permit() =>
        Assert.False(PermitWorkflow.Evaluate(NewPermit(PermitStatus.Draft), PermitAction.Submit, UserRole.Applicant, OtherApplicantId).Allowed);

    [Fact]
    public void Applicant_cannot_approve_their_own_permit() =>
        Assert.False(PermitWorkflow.Evaluate(NewPermit(PermitStatus.UnderReview, feePaid: true), PermitAction.Approve, UserRole.Applicant, ApplicantId).Allowed);

    [Fact]
    public void Inspector_cannot_review_applications() =>
        Assert.False(PermitWorkflow.Evaluate(NewPermit(PermitStatus.Submitted), PermitAction.StartReview, UserRole.Inspector, 4).Allowed);

    [Fact]
    public void Cannot_skip_review_and_approve_a_submitted_permit()
    {
        var check = PermitWorkflow.Evaluate(NewPermit(PermitStatus.Submitted, feePaid: true), PermitAction.Approve, UserRole.Clerk, ClerkId);
        Assert.False(check.Allowed);
        Assert.Equal(PermitStatus.Submitted, check.NewStatus);
    }

    [Fact]
    public void Approval_requires_fees_paid()
    {
        var unpaid = PermitWorkflow.Evaluate(NewPermit(PermitStatus.UnderReview), PermitAction.Approve, UserRole.Clerk, ClerkId);
        Assert.False(unpaid.Allowed);
        Assert.Contains("Fees", unpaid.Reason);

        Assert.True(PermitWorkflow.Evaluate(NewPermit(PermitStatus.UnderReview, feePaid: true), PermitAction.Approve, UserRole.Clerk, ClerkId).Allowed);
    }

    [Fact]
    public void Submission_requires_a_document_except_for_demolition()
    {
        var building = NewPermit(PermitStatus.Draft, withDocument: false);
        Assert.False(PermitWorkflow.Evaluate(building, PermitAction.Submit, UserRole.Applicant, ApplicantId).Allowed);

        var demolition = NewPermit(PermitStatus.Draft, withDocument: false);
        demolition.Type = PermitType.Demolition;
        Assert.True(PermitWorkflow.Evaluate(demolition, PermitAction.Submit, UserRole.Applicant, ApplicantId).Allowed);
    }

    [Theory]
    [InlineData(PermitStatus.Finaled)]
    [InlineData(PermitStatus.Rejected)]
    [InlineData(PermitStatus.Withdrawn)]
    public void Closed_permits_offer_no_actions(PermitStatus status)
    {
        Assert.Empty(PermitWorkflow.AvailableActions(NewPermit(status), UserRole.Applicant, ApplicantId));
        Assert.Empty(PermitWorkflow.AvailableActions(NewPermit(status), UserRole.Admin, ClerkId));
    }

    [Fact]
    public void Available_actions_include_blocked_ones_with_a_reason()
    {
        var actions = PermitWorkflow.AvailableActions(NewPermit(PermitStatus.UnderReview), UserRole.Clerk, ClerkId);
        Assert.Equal([PermitAction.RequestCorrections, PermitAction.Approve, PermitAction.Reject], actions.Select(a => a.Action));
        var approve = actions.Single(a => a.Action == PermitAction.Approve);
        Assert.False(approve.Allowed);
        Assert.NotNull(approve.Reason);
    }
}
