namespace AiVoicePortal.Api.Models;

public class InsurancePolicy
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string PolicyNumber { get; set; } = string.Empty;
    public string Tpa { get; set; } = string.Empty;
    public string MemberId { get; set; } = string.Empty;
    public DateTime ValidFrom { get; set; } = DateTime.UtcNow;
    public DateTime ValidTo { get; set; } = DateTime.UtcNow.AddYears(1);
    public decimal CoverageLimit { get; set; }
    public string Status { get; set; } = "Active";
    public string Eligibility { get; set; } = "Unknown";
    public string EligibilityNotes { get; set; } = string.Empty;
    public DateTime? EligibilityCheckedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Patient Patient { get; set; } = null!;
    public ICollection<InsuranceClaim> Claims { get; set; } = new List<InsuranceClaim>();
}

public class InsuranceClaim
{
    public int Id { get; set; }
    public int PolicyId { get; set; }
    public int PatientId { get; set; }
    public int? InvoiceId { get; set; }
    public string ClaimNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = "Draft";
    public string Notes { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? SubmittedAt { get; set; }

    public InsurancePolicy Policy { get; set; } = null!;
    public Patient Patient { get; set; } = null!;
    public Invoice? Invoice { get; set; }
}
