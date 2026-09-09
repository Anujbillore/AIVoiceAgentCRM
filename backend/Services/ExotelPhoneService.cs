using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using AiVoicePortal.Api.DTOs;

namespace AiVoicePortal.Api.Services;

public interface IExotelPhoneService
{
    bool IsConfigured { get; }
    string IncomingUrl { get; }
    Task<IReadOnlyList<ExotelNumberDto>> ListNumbersAsync(CancellationToken cancellationToken = default);
    Task<ExotelAttachResult> AttachIncomingAsync(string phoneSid, CancellationToken cancellationToken = default);
}

public class ExotelPhoneService : IExotelPhoneService
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;
    private readonly ILogger<ExotelPhoneService> _logger;

    public ExotelPhoneService(HttpClient http, IConfiguration config, ILogger<ExotelPhoneService> logger)
    {
        _http = http;
        _config = config;
        _logger = logger;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_config["Exotel:ApiKey"]) &&
        !string.IsNullOrWhiteSpace(_config["Exotel:ApiToken"]) &&
        !string.IsNullOrWhiteSpace(_config["Exotel:AccountSid"]);

    public string IncomingUrl
    {
        get
        {
            var root = (_config["Voice:PublicBaseUrl"] ?? string.Empty).TrimEnd('/');
            return string.IsNullOrWhiteSpace(root) ? string.Empty : $"{root}/api/voice/exotel/incoming";
        }
    }

    public async Task<IReadOnlyList<ExotelNumberDto>> ListNumbersAsync(CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        var sid = _config["Exotel:AccountSid"]!;
        var incoming = IncomingUrl;

        foreach (var url in CandidateListUrls(sid))
        {
            using var response = await SendAsync(HttpMethod.Get, url, null, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Exotel list numbers {Status} {Url}", response.StatusCode, url);
                continue;
            }

            var numbers = ParseNumbers(body, incoming);
            if (numbers.Count > 0)
            {
                return numbers;
            }
        }

        return [];
    }

    public async Task<ExotelAttachResult> AttachIncomingAsync(string phoneSid, CancellationToken cancellationToken = default)
    {
        EnsureConfigured();
        if (string.IsNullOrWhiteSpace(IncomingUrl))
        {
            return new ExotelAttachResult(false, phoneSid, "", "Set Voice:PublicBaseUrl to your ngrok https URL, then restart the API.");
        }

        var sid = _config["Exotel:AccountSid"]!;
        var attempts = new (HttpMethod Method, string Url, Func<HttpContent> Body)[]
        {
            (HttpMethod.Put, V2NumberUrl(sid, phoneSid), () => JsonBody(new { incoming_call_url = IncomingUrl })),
            (HttpMethod.Post, V2NumberUrl(sid, phoneSid), () => JsonBody(new { incoming_call_url = IncomingUrl, VoiceUrl = IncomingUrl })),
            (HttpMethod.Post, $"{V1NumberUrl(sid, phoneSid)}.json", () => FormBody(("VoiceUrl", IncomingUrl))),
            (HttpMethod.Post, V1NumberUrl(sid, phoneSid), () => FormBody(("VoiceUrl", IncomingUrl))),
        };

        string lastError = "Exotel did not accept a custom incoming URL.";
        foreach (var (method, url, bodyFactory) in attempts)
        {
            using var response = await SendAsync(method, url, bodyFactory(), cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode && !LooksLikeRejection(body) && await IsAttachedAsync(phoneSid, cancellationToken))
            {
                return new ExotelAttachResult(true, phoneSid, IncomingUrl, "ExoPhone now fetches the clinic ExoML incoming URL. Dial the number and you should hear Settings welcome + consent.");
            }

            lastError = FirstNonEmpty(body, lastError);
            _logger.LogWarning("Exotel attach failed {Status} {Url}", response.StatusCode, url);
        }

        return new ExotelAttachResult(
            false,
            phoneSid,
            IncomingUrl,
            "This Exotel trial cannot store a custom Voice URL (API returned 400). App Bazaar has no Use my own URL. Email Exotel support (account test6571) and ask them to set incoming Voice/ExoML for 08047283845 to: " + IncomingUrl);
    }

    private async Task<bool> IsAttachedAsync(string phoneSid, CancellationToken cancellationToken)
    {
        var numbers = await ListNumbersAsync(cancellationToken);
        return numbers.Any(n => n.Sid.Equals(phoneSid, StringComparison.OrdinalIgnoreCase) && n.AttachedToPortal);
    }

    private IEnumerable<string> CandidateListUrls(string sid)
    {
        yield return $"{ApiRoot()}/v2/accounts/{sid}/incoming-phone-numbers";
        yield return $"{ApiRoot()}/v2_beta/Accounts/{sid}/IncomingPhoneNumbers";
        yield return $"{ApiRoot()}/v1/Accounts/{sid}/IncomingPhoneNumbers.json";
        yield return $"{ApiRoot()}/v1/Accounts/{sid}/IncomingPhoneNumbers";
    }

    private string V2NumberUrl(string sid, string phoneSid) =>
        $"{ApiRoot()}/v2/accounts/{sid}/incoming-phone-numbers/{phoneSid}";

    private string V1NumberUrl(string sid, string phoneSid) =>
        $"{ApiRoot()}/v1/Accounts/{sid}/IncomingPhoneNumbers/{phoneSid}";

    private string ApiRoot()
    {
        var host = _config["Exotel:Subdomain"];
        if (string.IsNullOrWhiteSpace(host))
        {
            host = "api.in.exotel.com";
        }

        if (!host.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            host = "https://" + host.TrimEnd('/');
        }

        return host.TrimEnd('/');
    }

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, url) { Content = content };
        var raw = Encoding.ASCII.GetBytes($"{_config["Exotel:ApiKey"]}:{_config["Exotel:ApiToken"]}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", Convert.ToBase64String(raw));
        return await _http.SendAsync(request, cancellationToken);
    }

    private static List<ExotelNumberDto> ParseNumbers(string json, string incoming)
    {
        var results = new List<ExotelNumberDto>();
        if (string.IsNullOrWhiteSpace(json))
        {
            return results;
        }

        using var doc = JsonDocument.Parse(json);
        CollectNumbers(doc.RootElement, incoming, results);
        return results
            .GroupBy(n => n.Sid, StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .ToList();
    }

    private static void CollectNumbers(JsonElement element, string incoming, List<ExotelNumberDto> results)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                CollectNumbers(item, incoming, results);
            }

            return;
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        var sid = ReadString(element, "sid", "Sid", "phone_number_sid");
        var phone = ReadString(element, "phone_number", "PhoneNumber", "number", "friendly_name", "FriendlyName");
        var voice = ReadString(element, "incoming_call_url", "IncomingCallUrl", "VoiceUrl", "voice_url");
        if (!string.IsNullOrWhiteSpace(sid) && !string.IsNullOrWhiteSpace(phone))
        {
            var attached = !string.IsNullOrWhiteSpace(incoming)
                && voice.Contains("/api/voice/exotel/incoming", StringComparison.OrdinalIgnoreCase);
            results.Add(new ExotelNumberDto(sid, phone, voice, attached));
        }

        foreach (var prop in element.EnumerateObject())
        {
            if (prop.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
            {
                CollectNumbers(prop.Value, incoming, results);
            }
        }
    }

    private static string ReadString(JsonElement element, params string[] names)
    {
        foreach (var name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
            {
                return value.GetString() ?? string.Empty;
            }
        }

        return string.Empty;
    }

    private static bool LooksLikeRejection(string body) =>
        body.Contains("invalid", StringComparison.OrdinalIgnoreCase)
        && (body.Contains("url", StringComparison.OrdinalIgnoreCase) || body.Contains("VoiceUrl", StringComparison.OrdinalIgnoreCase));

    private static StringContent JsonBody(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static FormUrlEncodedContent FormBody(params (string Key, string Value)[] pairs) =>
        new(pairs.Select(p => new KeyValuePair<string, string>(p.Key, p.Value)));

    private static string FirstNonEmpty(params string[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? string.Empty;

    private void EnsureConfigured()
    {
        if (!IsConfigured)
        {
            throw new InvalidOperationException("Set Exotel:ApiKey, Exotel:ApiToken, and Exotel:AccountSid in appsettings.Local.json.");
        }
    }
}
