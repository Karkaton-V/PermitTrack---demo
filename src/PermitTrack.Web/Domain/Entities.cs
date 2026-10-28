using System.ComponentModel.DataAnnotations;

namespace PermitTrack.Web.Domain;

public class AppUser
{
    public int Id { get; set; }
    [MaxLength(100)] public required string Name { get; set; }
    [MaxLength(200)] public required string Email { get; set; }
    public UserRole Role { get; set; }
    [MaxLength(100)] public string? Company { get; set; }
}

public class Permit
{
    public int Id { get; set; }
    [MaxLength(20)] public string Number { get; set; } = "";
    public PermitType Type { get; set; }
    public PermitStatus Status { get; set; } = PermitStatus.Draft;

    [MaxLength(200)] public required string Address { get; set; }
    [MaxLength(30)] public string? ParcelNumber { get; set; }
    [MaxLength(2000)] public required string Description { get; set; }
    public decimal Valuation { get; set; }
    [MaxLength(100)] public string? ContractorName { get; set; }
    [MaxLength(30)] public string? ContractorLicense { get; set; }

    public decimal FeeTotal { get; set; }
    public DateTime? FeePaidAt { get; set; }
    [MaxLength(50)] public string? PaymentReference { get; set; }
    public bool FeePaid => FeePaidAt is not null;

    public int ApplicantId { get; set; }
    public AppUser? Applicant { get; set; }
    public int? ReviewerId { get; set; }
    public AppUser? Reviewer { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? SubmittedAt { get; set; }
    public DateTime? IssuedAt { get; set; }
    public DateTime? FinaledAt { get; set; }

    public List<Inspection> Inspections { get; set; } = [];
    public List<PermitDocument> Documents { get; set; } = [];
    public List<PermitEvent> Events { get; set; } = [];
}

public class Inspection
{
    public int Id { get; set; }
    public int PermitId { get; set; }
    public Permit? Permit { get; set; }
    public InspectionType Type { get; set; }
    public DateOnly ScheduledFor { get; set; }
    public int? InspectorId { get; set; }
    public AppUser? Inspector { get; set; }
    public InspectionResult Result { get; set; } = InspectionResult.Scheduled;
    [MaxLength(2000)] public string? Notes { get; set; }
    public DateTime? CompletedAt { get; set; }
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
}

public class PermitDocument
{
    public int Id { get; set; }
    public int PermitId { get; set; }
    public Permit? Permit { get; set; }
    [MaxLength(255)] public required string FileName { get; set; }
    [MaxLength(64)] public required string StoredName { get; set; }
    [MaxLength(100)] public required string ContentType { get; set; }
    public long SizeBytes { get; set; }
    public DateTime UploadedAt { get; set; }
    public int UploadedById { get; set; }
    public AppUser? UploadedBy { get; set; }
}

/// <summary>Append-only audit trail entry for a permit.</summary>
public class PermitEvent
{
    public int Id { get; set; }
    public int PermitId { get; set; }
    public Permit? Permit { get; set; }
    public DateTime At { get; set; }
    public int? UserId { get; set; }
    public AppUser? User { get; set; }
    [MaxLength(200)] public required string Action { get; set; }
    [MaxLength(2000)] public string? Note { get; set; }
}
