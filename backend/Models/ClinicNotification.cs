namespace AiVoicePortal.Api.Models;

public class ClinicNotification
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Tag { get; set; } = "General";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsRead { get; set; }
    public string Audience { get; set; } = "Staff";
    public int? DoctorId { get; set; }
    public string? RelatedType { get; set; }
    public int? RelatedId { get; set; }
}
