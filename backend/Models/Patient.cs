namespace AiVoicePortal.Api.Models;

public class Patient
{
    public int Id { get; set; }
    public string? UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Age { get; set; }
    public string Contact { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public string Uhid { get; set; } = string.Empty;
    public string Gender { get; set; } = string.Empty;
    public string BloodGroup { get; set; } = string.Empty;
    public string EmergencyName { get; set; } = string.Empty;
    public string EmergencyPhone { get; set; } = string.Empty;
    public string Allergies { get; set; } = string.Empty;
    public bool IsVip { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public ApplicationUser? User { get; set; }
    public ICollection<Appointment> Appointments { get; set; } = new List<Appointment>();
    public ICollection<PatientDocument> Documents { get; set; } = new List<PatientDocument>();
    public ICollection<PatientCharge> Charges { get; set; } = new List<PatientCharge>();
}
