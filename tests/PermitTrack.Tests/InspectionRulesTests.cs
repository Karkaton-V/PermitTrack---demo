using PermitTrack.Web.Domain;

namespace PermitTrack.Tests;

public class InspectionRulesTests
{
    private static readonly DateTime Issued = new(2026, 3, 2, 10, 0, 0);

    private static Permit IssuedBuilding() => new()
    {
        Address = "1 Main St", Description = "Test", Type = PermitType.Building,
        Status = PermitStatus.Issued, IssuedAt = Issued
    };

    private static Inspection Done(InspectionType type, InspectionResult result, DateTime at) =>
        new() { Type = type, Result = result, CompletedAt = at, ScheduledFor = DateOnly.FromDateTime(at) };

    [Fact]
    public void Only_the_first_inspection_is_schedulable_on_a_new_permit() =>
        Assert.Equal([InspectionType.Footing], InspectionRules.Schedulable(IssuedBuilding()));

    [Fact]
    public void Next_inspection_unlocks_after_previous_passes()
    {
        var p = IssuedBuilding();
        p.Inspections.Add(Done(InspectionType.Footing, InspectionResult.Passed, Issued.AddDays(5)));
        Assert.Equal([InspectionType.Framing], InspectionRules.Schedulable(p));
    }

    [Fact]
    public void Failed_inspection_can_be_requested_again()
    {
        var p = IssuedBuilding();
        p.Inspections.Add(Done(InspectionType.Footing, InspectionResult.Failed, Issued.AddDays(5)));
        Assert.Equal([InspectionType.Footing], InspectionRules.Schedulable(p));
    }

    [Fact]
    public void Nothing_schedulable_while_an_inspection_is_pending()
    {
        var p = IssuedBuilding();
        p.Inspections.Add(new Inspection { Type = InspectionType.Footing, ScheduledFor = new DateOnly(2026, 3, 10) });
        Assert.Empty(InspectionRules.Schedulable(p));
    }

    [Theory]
    [InlineData(PermitStatus.Draft)]
    [InlineData(PermitStatus.UnderReview)]
    [InlineData(PermitStatus.Finaled)]
    public void Nothing_schedulable_unless_issued(PermitStatus status)
    {
        var p = IssuedBuilding();
        p.Status = status;
        Assert.Empty(InspectionRules.Schedulable(p));
    }

    [Fact]
    public void All_required_passed_detects_completion()
    {
        var p = new Permit { Address = "x", Description = "x", Type = PermitType.Electrical, Status = PermitStatus.Issued, IssuedAt = Issued };
        p.Inspections.Add(Done(InspectionType.RoughElectrical, InspectionResult.Passed, Issued.AddDays(3)));
        Assert.False(InspectionRules.AllRequiredPassed(p));
        p.Inspections.Add(Done(InspectionType.FinalElectrical, InspectionResult.Passed, Issued.AddDays(9)));
        Assert.True(InspectionRules.AllRequiredPassed(p));
    }

    [Fact]
    public void Expiration_is_180_days_from_issuance_without_activity() =>
        Assert.Equal(Issued.AddDays(180), InspectionRules.ExpiresOn(IssuedBuilding()));

    [Fact]
    public void Passed_inspection_extends_expiration_but_failed_does_not()
    {
        var p = IssuedBuilding();
        p.Inspections.Add(Done(InspectionType.Footing, InspectionResult.Failed, Issued.AddDays(30)));
        Assert.Equal(Issued.AddDays(180), InspectionRules.ExpiresOn(p));

        p.Inspections.Add(Done(InspectionType.Footing, InspectionResult.Passed, Issued.AddDays(60)));
        Assert.Equal(Issued.AddDays(240), InspectionRules.ExpiresOn(p));
    }

    [Fact]
    public void Review_is_overdue_after_target_days()
    {
        var p = new Permit { Address = "x", Description = "x", Status = PermitStatus.Submitted, SubmittedAt = Issued };
        Assert.False(InspectionRules.IsReviewOverdue(p, Issued.AddDays(InspectionRules.ReviewTargetDays)));
        Assert.True(InspectionRules.IsReviewOverdue(p, Issued.AddDays(InspectionRules.ReviewTargetDays + 1)));
    }
}
