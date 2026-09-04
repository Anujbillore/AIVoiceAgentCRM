namespace AiVoicePortal.Api.Models;

public class PatientCharge
{
    public int Id { get; set; }
    public int PatientId { get; set; }
    public int? AppointmentId { get; set; }
    public string Kind { get; set; } = "Billing";
    public string Title { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Notes { get; set; } = string.Empty;
    public DateTime ChargeDate { get; set; } = DateTime.UtcNow;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public Patient Patient { get; set; } = null!;
    public Appointment? Appointment { get; set; }
}
