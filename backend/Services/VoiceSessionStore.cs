using System.Collections.Concurrent;

namespace AiVoicePortal.Api.Services;

public class VoiceSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..16];
    public string? ExternalCallId { get; set; }
    public string CallerName { get; set; } = "Unknown";
    public string CallerPhone { get; set; } = string.Empty;
    public int? DoctorId { get; set; }
    public int? PatientId { get; set; }
    public DateTime? PreferredTime { get; set; }
    public string? AppointmentType { get; set; }
    public string? Symptoms { get; set; }
    public string? PatientUhid { get; set; }
    public bool Ended { get; set; }
    public bool Logged { get; set; }
    public bool ConsentGiven { get; set; }
    public bool AfterHours { get; set; }
    public bool IsVip { get; set; }
    public int ClarifyingQuestions { get; set; }
    public double LastConfidence { get; set; } = 1;
    public string Phase { get; set; } = "Greeting";
    public string Outcome { get; set; } = "";
    public string EscalationReason { get; set; } = "";
    public string TransferType { get; set; } = "None";
    public string Sentiment { get; set; } = "Neutral";
    public byte[]? ReplyAudio { get; set; }
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public List<VoiceChatTurn> Turns { get; } = [];
}

public record VoiceChatTurn(string Role, string Text, string Intent);

public interface IVoiceSessionStore
{
    VoiceSession Create(string callerName, string callerPhone, string? externalCallId = null);
    VoiceSession? Get(string id);
    VoiceSession? GetByExternal(string externalCallId);
}

public class VoiceSessionStore : IVoiceSessionStore
{
    private readonly ConcurrentDictionary<string, VoiceSession> _sessions = new();

    public VoiceSession Create(string callerName, string callerPhone, string? externalCallId = null)
    {
        var session = new VoiceSession
        {
            CallerName = string.IsNullOrWhiteSpace(callerName) ? "Unknown" : callerName,
            CallerPhone = callerPhone ?? string.Empty,
            ExternalCallId = externalCallId
        };
        _sessions[session.Id] = session;
        if (!string.IsNullOrWhiteSpace(externalCallId))
        {
            _sessions[externalCallId] = session;
        }

        return session;
    }

    public VoiceSession? Get(string id) =>
        _sessions.TryGetValue(id, out var session) ? session : null;

    public VoiceSession? GetByExternal(string externalCallId) =>
        string.IsNullOrWhiteSpace(externalCallId) ? null : Get(externalCallId);
}
