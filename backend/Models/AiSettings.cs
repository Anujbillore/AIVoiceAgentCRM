namespace AiVoicePortal.Api.Models;

public class AiSettings
{
    public int Id { get; set; }
    public string AgentName { get; set; } = "Anuj's AI Assistant";
    public string WelcomeMessage { get; set; } = "Hello, thank you for calling. How can I help you today?";
    public string Language { get; set; } = "en-IN";
    public string Instructions { get; set; } = "You are a polite clinic receptionist. Help patients book appointments, answer queries, and politely end spam calls.";
    public string VoiceSpeaker { get; set; } = "ritu";
    public string ConsentMessage { get; set; } = "This call may be recorded and handled by our clinic AI. You can ask for a person at any time.";
    public string TransferNumber { get; set; } = string.Empty;
}
