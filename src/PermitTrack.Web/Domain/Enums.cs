namespace PermitTrack.Web.Domain;

public enum UserRole
{
    Applicant,
    Clerk,
    Inspector,
    Admin
}

public enum PermitType
{
    Building,
    Electrical,
    Plumbing,
    Mechanical,
    Demolition
}

public enum PermitStatus
{
    Draft,
    Submitted,
    UnderReview,
    CorrectionsRequired,
    Issued,
    Finaled,
    Rejected,
    Withdrawn
}

public enum PermitAction
{
    Submit,
    StartReview,
    RequestCorrections,
    Resubmit,
    Approve,
    Reject,
    Withdraw
}

public enum InspectionType
{
    Footing,
    Framing,
    Insulation,
    FinalBuilding,
    RoughElectrical,
    FinalElectrical,
    RoughPlumbing,
    FinalPlumbing,
    RoughMechanical,
    FinalMechanical,
    SiteClearance
}

public enum InspectionResult
{
    Scheduled,
    Passed,
    Failed,
    Cancelled
}
