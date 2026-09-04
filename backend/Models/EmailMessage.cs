namespace AiVoicePortal.Api.Models;

public class EmailMessage
{
    public int Id { get; set; }
    public string Recipient { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public DateTime SentAt { get; set; } = DateTime.UtcNow;
    public string Delivery { get; set; } = "Logged";
}
