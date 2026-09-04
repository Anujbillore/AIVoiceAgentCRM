namespace AiVoicePortal.Api.Models;

public class CallCallback
{
    public int Id { get; set; }
    public string CallerName { get; set; } = "Unknown";
    public string CallerPhone { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTime? PreferredTime { get; set; }
    public string Status { get; set; } = "Queued";
    public bool Priority { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
