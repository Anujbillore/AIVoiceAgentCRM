namespace AiVoicePortal.Api.Models;

public class CallLog
{
    public int Id { get; set; }
    public string CallerName { get; set; } = "Unknown";
    public string CallerPhone { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string ActionTaken { get; set; } = string.Empty;
    public string Intent { get; set; } = "Query";
    public string Transcript { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string Outcome { get; set; } = "Contained";
    public string EscalationReason { get; set; } = string.Empty;
    public string TransferType { get; set; } = "None";
    public double Confidence { get; set; } = 1;
    public string Sentiment { get; set; } = "Neutral";
    public bool ConsentGiven { get; set; } = true;
    public int? PatientId { get; set; }
}
