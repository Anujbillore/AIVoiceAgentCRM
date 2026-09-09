using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiVoicePortal.Api.Models;

namespace AiVoicePortal.Api.Services;

public record IntentResult(string Intent, string Summary, string ActionHint, string Reply, double Confidence = 0.75);

public interface ISarvamAiService
{
    bool IsConfigured { get; }
    Task<string> TranscribeAsync(Stream audio, string fileName, string contentType, CancellationToken cancellationToken = default);
    Task<string?> SynthesizeBase64Async(string text, string languageCode, string speaker, CancellationToken cancellationToken = default);
    Task<IntentResult> DetectIntentAsync(string transcript, AiSettings settings, CancellationToken cancellationToken = default);
    Task<string> ClassifyDocumentAsync(string fileName, CancellationToken cancellationToken = default);
    Task<string> TranslateToEnglishAsync(string text, CancellationToken cancellationToken = default);
    Task<bool> EnsureEnglishAsync(CallLog call, CancellationToken cancellationToken = default);
}

public class SarvamAiService : ISarvamAiService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<SarvamAiService> _logger;

    public SarvamAiService(HttpClient http, IConfiguration config, ILogger<SarvamAiService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
        _http.BaseAddress = new Uri("https://api.sarvam.ai/");
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_config["Sarvam:ApiKey"]);

    public async Task<string> TranscribeAsync(Stream audio, string fileName, string contentType, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        using var form = new MultipartFormDataContent();
        var streamContent = new StreamContent(audio);
        streamContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(streamContent, "file", fileName);
        form.Add(new StringContent(_config["Sarvam:SttModel"] ?? "saaras:v4"), "model");
        form.Add(new StringContent("transcribe"), "mode");
        form.Add(new StringContent("unknown"), "language_code");

        using var request = new HttpRequestMessage(HttpMethod.Post, "speech-to-text") { Content = form };
        AddApiKey(request);

        var response = await _http.SendAsync(request, cancellationToken);
        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Sarvam STT failed: {Status} {Body}", response.StatusCode, json);
            return string.Empty;
        }

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("transcript", out var transcript))
        {
            return transcript.GetString() ?? string.Empty;
        }

        return string.Empty;
    }

    public async Task<string?> SynthesizeBase64Async(string text, string languageCode, string speaker, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured || string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var payload = new
        {
            text,
            language_code = languageCode,
            speaker,
            model = _config["Sarvam:TtsModel"] ?? "bulbul:v3"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "text-to-speech")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        AddApiKey(request);

        var response = await _http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning("Sarvam TTS failed: {Status} {Body}", response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
            return null;
        }

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (doc.RootElement.TryGetProperty("audios", out var audios) && audios.GetArrayLength() > 0)
        {
            return audios[0].GetString();
        }

        return null;
    }

    public async Task<IntentResult> DetectIntentAsync(string transcript, AiSettings settings, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return FallbackIntent(transcript);
        }

        var system = $"""
            You are {settings.AgentName}, a clinic voice receptionist.
            Language: {settings.Language} (reply in the caller's language when it matches one of these).
            Instructions: {settings.Instructions}

            Classify the caller utterance and reply briefly.
            Return ONLY valid JSON with keys:
            intent (Appointment | Reschedule | Cancel | Query | Billing | Spam | Human | Emergency | Callback),
            summary (one sentence),
            actionHint (what the clinic should record as Action Taken),
            reply (spoken reply to the caller),
            confidence (0 to 1).
            """;

        var payload = new
        {
            model = _config["Sarvam:ChatModel"] ?? "sarvam-105b",
            temperature = 0.2,
            messages = new object[]
            {
                new { role = "system", content = system },
                new { role = "user", content = transcript }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        AddApiKey(request);

        try
        {
            var response = await _http.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            response.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(json);
            var content = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? string.Empty;

            var parsed = TryParseIntent(content);
            return parsed ?? FallbackIntent(transcript);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sarvam intent detection failed; using fallback.");
            return FallbackIntent(transcript);
        }
    }

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Sarvam API key is not configured. Set Sarvam:ApiKey in appsettings or environment.");
        }
    }

    private void AddApiKey(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("api-subscription-key", _config["Sarvam:ApiKey"]);
    }

    private static IntentResult? TryParseIntent(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(content[start..(end + 1)]);
            var root = doc.RootElement;
            var intent = root.GetProperty("intent").GetString() ?? "Query";
            var summary = root.GetProperty("summary").GetString() ?? content;
            var action = root.TryGetProperty("actionHint", out var a) ? a.GetString() ?? "" : "";
            var reply = root.TryGetProperty("reply", out var r) ? r.GetString() ?? "" : "";
            var confidence = 0.8;
            if (root.TryGetProperty("confidence", out var c))
            {
                if (c.ValueKind == JsonValueKind.Number)
                {
                    confidence = c.GetDouble();
                }
                else if (double.TryParse(c.GetString(), out var parsed))
                {
                    confidence = parsed;
                }
            }

            return new IntentResult(NormalizeIntent(intent), summary, action, reply, Math.Clamp(confidence, 0, 1));
        }
        catch
        {
            return null;
        }
    }

    public static IntentResult FallbackIntent(string transcript)
    {
        var text = transcript.ToLowerInvariant();
        if (text.Contains("spam") || text.Contains("lottery") || text.Contains("prize") || text.Contains("offer"))
        {
            return new IntentResult(
                "Spam",
                "Spam call",
                "Ended politely",
                "I am sorry, this line is only for clinic appointments. Goodbye.",
                0.9);
        }

        if (text.Contains("reschedul") || text.Contains("change my appointment") || text.Contains("move my appointment"))
        {
            return new IntentResult("Reschedule", transcript, "Reschedule appointment", "I can reschedule that for you.", 0.82);
        }

        if (text.Contains("cancel my appointment") || text.Contains("cancel the appointment"))
        {
            return new IntentResult("Cancel", transcript, "Cancel appointment", "I can cancel that appointment.", 0.82);
        }

        if (text.Contains("bill") || text.Contains("invoice") || text.Contains("balance") || text.Contains("payment due"))
        {
            return new IntentResult("Billing", transcript, "Looked up billing", "I can check your balance.", 0.8);
        }

        if (text.Contains("callback") || text.Contains("call me back"))
        {
            return new IntentResult("Callback", transcript, "Queued callback", "I can arrange a callback.", 0.85);
        }

        if (text.Contains("appoint") || text.Contains("book") || text.Contains("slot") || (text.Contains("doctor") && !text.Contains("speak to")))
        {
            return new IntentResult(
                "Appointment",
                $"Asked for an appointment: {transcript}",
                "Book appointment",
                "I can help you book that appointment. Please hold while I confirm availability.",
                0.78);
        }

        if (transcript.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length <= 2)
        {
            return new IntentResult("Query", transcript, "Needs clarification", "Could you tell me a bit more about how I can help?", 0.45);
        }

        return new IntentResult(
            "Query",
            transcript,
            "Logged query",
            "Thank you. I have noted your query and our staff will follow up if needed.",
            0.62);
    }

    private static string NormalizeIntent(string intent)
    {
        if (intent.Contains("emerg", StringComparison.OrdinalIgnoreCase)) return "Emergency";
        if (intent.Contains("human", StringComparison.OrdinalIgnoreCase) || intent.Contains("agent", StringComparison.OrdinalIgnoreCase) || intent.Contains("transfer", StringComparison.OrdinalIgnoreCase)) return "Human";
        if (intent.Contains("resched", StringComparison.OrdinalIgnoreCase)) return "Reschedule";
        if (intent.Contains("cancel", StringComparison.OrdinalIgnoreCase)) return "Cancel";
        if (intent.Contains("bill", StringComparison.OrdinalIgnoreCase) || intent.Contains("insur", StringComparison.OrdinalIgnoreCase)) return "Billing";
        if (intent.Contains("callback", StringComparison.OrdinalIgnoreCase)) return "Callback";
        if (intent.Contains("appoint", StringComparison.OrdinalIgnoreCase)) return "Appointment";
        if (intent.Contains("spam", StringComparison.OrdinalIgnoreCase)) return "Spam";
        return "Query";
    }

    public async Task<string> ClassifyDocumentAsync(string fileName, CancellationToken cancellationToken = default)
    {
        var fallback = ClassifyByFileName(fileName);
        if (!IsConfigured)
        {
            return fallback;
        }

        var payload = new
        {
            model = _config["Sarvam:ChatModel"] ?? "sarvam-105b",
            temperature = 0.1,
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = "Classify a hospital patient document into exactly one category: Aadhaar, Reports, Prescriptions, Insurance, Billing, Daily bills, Discharge, Consent, Other. Reply with only that category name."
                },
                new { role = "user", content = $"File name: {fileName}" }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        AddApiKey(request);

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(8));
            var response = await _http.SendAsync(request, timeout.Token);
            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Sarvam document classify failed: {Status} {Payload}", response.StatusCode, json);
                return fallback;
            }

            using var document = JsonDocument.Parse(json);
            var text = document.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString() ?? fallback;
            return NormalizeDocumentCategory(text, fallback);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Sarvam document classify failed");
            return fallback;
        }
    }

    public static string ClassifyByFileName(string fileName)
    {
        var name = fileName.ToLowerInvariant();
        if (name.Contains("daily bill") || name.Contains("dailybill") || name.Contains("day bill") || name.Contains("ward charge") || name.Contains("daily charge"))
        {
            return "Daily bills";
        }

        if (name.Contains("bill") || name.Contains("invoice") || name.Contains("receipt") || name.Contains("payment") || name.Contains("gst") || name.Contains("charges"))
        {
            return "Billing";
        }

        if (name.Contains("insurance") || name.Contains("policy") || name.Contains("tpa") || name.Contains("claim") || name.Contains("preauth") || name.Contains("pre-auth") || name.Contains("cashless"))
        {
            return "Insurance";
        }

        if (name.Contains("discharge") || name.Contains("admission") || name.Contains("dama"))
        {
            return "Discharge";
        }

        if (name.Contains("consent") || name.Contains("undertaking") || name.Contains("authorization"))
        {
            return "Consent";
        }

        if (name.Contains("aadhar") || name.Contains("aadhaar") || name.Contains("uidai") || name.Contains("passport") || name.Contains("pan card") || name.Contains("voter"))
        {
            return "Aadhaar";
        }

        if (name.Contains("report") || name.Contains("lab") || name.Contains("scan") || name.Contains("xray") || name.Contains("x-ray") || name.Contains("mri") || name.Contains("blood") || name.Contains("patholog") || name.Contains("ct scan"))
        {
            return "Reports";
        }

        if (name.Contains("prescription") || name.Contains("rx") || name.Contains("medicine"))
        {
            return "Prescriptions";
        }

        return "Other";
    }

    private static string NormalizeDocumentCategory(string value, string fallback)
    {
        var text = value.Trim().ToLowerInvariant();
        if (text.Contains("daily"))
        {
            return "Daily bills";
        }

        if (text.Contains("bill") || text.Contains("invoice") || text.Contains("receipt"))
        {
            return "Billing";
        }

        if (text.Contains("insurance") || text.Contains("tpa") || text.Contains("claim"))
        {
            return "Insurance";
        }

        if (text.Contains("discharge") || text.Contains("admission"))
        {
            return "Discharge";
        }

        if (text.Contains("consent"))
        {
            return "Consent";
        }

        if (text.Contains("aadhar") || text.Contains("aadhaar") || text.Contains("identity"))
        {
            return "Aadhaar";
        }

        if (text.Contains("report") || text.Contains("lab"))
        {
            return "Reports";
        }

        if (text.Contains("prescription") || text.Contains("rx"))
        {
            return "Prescriptions";
        }

        if (text.Contains("other"))
        {
            return "Other";
        }

        return fallback;
    }

    public async Task<string> TranslateToEnglishAsync(string text, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text) || !CallLogDetails.HasIndic(text))
        {
            return text;
        }

        if (!IsConfigured)
        {
            return text;
        }

        var chunks = new List<string>();
        for (var i = 0; i < text.Length; i += 900)
        {
            var chunk = text.Substring(i, Math.Min(900, text.Length - i));
            var payload = new
            {
                input = chunk,
                source_language_code = "hi-IN",
                target_language_code = "en-IN",
                model = "mayura:v1",
                mode = "formal"
            };
            using var request = new HttpRequestMessage(HttpMethod.Post, "translate")
            {
                Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
            };
            AddApiKey(request);
            var response = await _http.SendAsync(request, cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Sarvam translate failed: {Status} {Body}", response.StatusCode, json);
                chunks.Add(chunk);
                continue;
            }

            using var doc = JsonDocument.Parse(json);
            chunks.Add(doc.RootElement.TryGetProperty("translated_text", out var translated)
                ? translated.GetString() ?? chunk
                : chunk);
        }

        return string.Concat(chunks);
    }

    public async Task<bool> EnsureEnglishAsync(CallLog call, CancellationToken cancellationToken = default)
    {
        var changed = false;
        var summary = await TranslateToEnglishAsync(call.Summary, cancellationToken);
        var action = await TranslateToEnglishAsync(call.ActionTaken, cancellationToken);
        var transcript = await TranslateToEnglishAsync(call.Transcript, cancellationToken);
        var name = await TranslateToEnglishAsync(call.CallerName, cancellationToken);
        if (summary != call.Summary) { call.Summary = summary; changed = true; }
        if (action != call.ActionTaken) { call.ActionTaken = action; changed = true; }
        if (transcript != call.Transcript) { call.Transcript = transcript; changed = true; }
        if (name != call.CallerName) { call.CallerName = name; changed = true; }
        return changed;
    }
}
