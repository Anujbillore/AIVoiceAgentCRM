using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/webhooks/sarvam-managed")]
public class SarvamManagedWebhookController : ControllerBase
{
    private readonly IConfiguration _config;
    private readonly ISarvamManagedService _managed;
    private readonly AppDbContext _db;
    private readonly InboundCallerContext _inbound;
    private readonly ILogger<SarvamManagedWebhookController> _logger;

    public SarvamManagedWebhookController(
        IConfiguration config,
        ISarvamManagedService managed,
        AppDbContext db,
        InboundCallerContext inbound,
        ILogger<SarvamManagedWebhookController> logger)
    {
        _config = config;
        _managed = managed;
        _db = db;
        _inbound = inbound;
        _logger = logger;
    }

    [AcceptVerbs("GET", "POST")]
    [Route("agent-context")]
    public async Task<IActionResult> AgentContext([FromQuery] string? key, CancellationToken cancellationToken)
    {
        if (!SecretMatches(key))
        {
            return Unauthorized();
        }

        RememberInboundCaller();
        var settings = await _db.AiSettings.FirstAsync(cancellationToken);
        var roster = await _managed.GetClinicRosterAsync(cancellationToken);
        var instructions = AppendBookingGuidance(settings.Instructions, roster);
        return Ok(new
        {
            agent_name = settings.AgentName,
            agent_names = settings.AgentName,
            welcome_message = $"{settings.WelcomeMessage} {settings.ConsentMessage}".Trim(),
            welcome_messages = settings.WelcomeMessage,
            agent_instructions = instructions,
            agent_instruction = instructions,
            clinic_doctors = roster.Roster,
            doctor_roster = roster.Roster,
            allowed_doctor_names = roster.AllowedNames,
            doctors = roster.Doctors.Select(d => new { doctor_name = d.DoctorName, specialization = d.Specialization }),
            default_language = settings.Language.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "en-IN",
            supported_languages = settings.Language,
            voice = settings.VoiceSpeaker,
            transfer_number = settings.TransferNumber
        });
    }

    [AcceptVerbs("GET", "POST")]
    [Route("check-availability")]
    public async Task<IActionResult> CheckAvailability(
        [FromQuery] string? key,
        [FromQuery] string? date,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SarvamAvailabilityRequest? payload,
        CancellationToken cancellationToken)
    {
        if (!SecretMatches(key))
        {
            _logger.LogWarning("Availability webhook rejected: secret mismatch");
            return Unauthorized();
        }

        var rawDate = FirstNonEmpty(
            payload?.Date,
            payload?.RequestedDate,
            date,
            Request.Query["requested_date"].ToString(),
            ReadForm("date", "requested_date"),
            payload?.ExtraDate);
        if (!TryParseLocalDate(rawDate, out var parsedDate))
        {
            return Ok(new
            {
                available = false,
                slots = Array.Empty<object>(),
                message = "Ask the caller for a calendar date, then retry with yyyy-MM-dd. Do not end the call."
            });
        }

        try
        {
            var result = await _managed.GetAvailabilityAsync(
                parsedDate,
                payload?.DurationMinutes,
                FirstNonEmpty(payload?.Problem, payload?.Purpose, payload?.Reason),
                FirstNonEmpty(
                    payload?.DoctorName,
                    payload?.SelectedDoctor,
                    Request.Query["doctor_name"].ToString(),
                    ReadForm("doctor_name", "selected_doctor")),
                FirstNonEmpty(
                    payload?.PreferredTime,
                    payload?.Time,
                    Request.Query["preferred_time"].ToString(),
                    Request.Query["time"].ToString(),
                    ReadForm("preferred_time", "time")),
                cancellationToken);
            var names = result.Doctors.Select(d => d.DoctorName).ToArray();
            var doctorsToSay = names.Length == 0
                ? "none"
                : names.Length == 1
                    ? names[0]
                    : string.Join(" and ", names);
            var spoken = string.IsNullOrWhiteSpace(result.SpokenPrompt) ? result.Message : result.SpokenPrompt;
            _logger.LogInformation(
                "Availability {Date} specialty={Specialty} choice={NeedsChoice} doctors={Doctors} slots={Slots}",
                parsedDate,
                result.MatchedSpecialty,
                result.NeedsDoctorChoice,
                result.Doctors.Count,
                result.Slots.Count);
            return Ok(new
            {
                value = spoken,
                text = spoken,
                doctors_to_say = doctorsToSay,
                available = result.Available,
                date = parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                matched_specialty = result.MatchedSpecialty,
                needs_doctor_choice = result.NeedsDoctorChoice,
                doctor_count = result.Doctors.Count,
                available_doctor_names = names,
                spoken_prompt = spoken,
                doctors = result.Doctors.Select(d => new
                {
                    doctor_id = d.DoctorId,
                    doctor_name = d.DoctorName,
                    specialization = d.Specialization,
                    slot_count = d.SlotCount
                }),
                slots = result.Slots.Select(x => new { local_start = x.LocalStart, display = x.Display, doctor_name = x.DoctorName }),
                message = result.Message
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Availability check failed for {Date}", rawDate);
            return Ok(new
            {
                value = "The calendar could not be checked right now. Apologize and do not end the call.",
                available = false,
                doctors_to_say = "none",
                slots = Array.Empty<object>(),
                message = "The calendar could not be checked right now. Apologize and do not end the call."
            });
        }
    }

    [AcceptVerbs("GET", "POST")]
    [Route("book-appointment")]
    public async Task<IActionResult> BookAppointment(
        [FromQuery] string? key,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SarvamBookAppointmentRequest? payload,
        CancellationToken cancellationToken)
    {
        if (!SecretMatches(key))
        {
            return Unauthorized();
        }

        var rawStart = FirstNonEmpty(payload?.LocalStart, payload?.SelectedLocalStart);
        if (payload is null || !TryParseLocalDateTime(rawStart, out var localStart))
        {
            return Ok(new
            {
                booked = false,
                message = "Need a confirmed slot as yyyy-MM-ddTHH:mm:ss. Ask again and do not end the call."
            });
        }

        try
        {
            RememberInboundCaller();
            var result = await _managed.BookAsync(
                localStart,
                FirstNonEmpty(payload.CallerName, _inbound.RecentName()) ?? string.Empty,
                FirstNonEmpty(
                    payload.CallerPhone,
                    ReadJsonString(payload.Extra, "user_phone_number", "caller_phone_number", "phone", "from"),
                    ReadCallerFromRequest(),
                    _inbound.RecentPhone()) ?? string.Empty,
                FirstNonEmpty(payload.Purpose, payload.AppointmentPurpose) ?? string.Empty,
                FirstNonEmpty(payload.DoctorName, payload.SelectedDoctor),
                cancellationToken);
            _logger.LogInformation("Book {Start} booked={Booked} id={Id} doctor={Doctor}", localStart, result.Booked, result.AppointmentId, result.DoctorName);
            return Ok(new
            {
                value = result.Message,
                booked = result.Booked,
                appointment_id = result.AppointmentId,
                local_start = result.LocalStart,
                doctor_name = result.DoctorName,
                message = result.Message,
                alternative_slots = result.Alternatives.Select(x => new { local_start = x.LocalStart, display = x.Display })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Appointment booking failed for {LocalStart}", payload.LocalStart);
            return Ok(new
            {
                booked = false,
                message = "The appointment could not be booked right now. Offer to take a message and do not end the call."
            });
        }
    }

    [HttpPost("agent-ended")]
    public async Task<IActionResult> AgentEnded(
        [FromQuery] string? key,
        [FromBody] SarvamAgentEndedPayload payload,
        CancellationToken cancellationToken)
    {
        if (!SecretMatches(key))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(payload.CallSummary))
        {
            return BadRequest(new { error = "call_summary is required" });
        }

        var attemptId = !string.IsNullOrWhiteSpace(payload.InteractionId)
            ? payload.InteractionId.Trim()
            : CreateStableAttemptId(payload.CallerPhoneNumber ?? payload.CallbackNumber, payload.StartDateTime, payload.CallSummary);
        var result = await _managed.ImportCallAsync(
            new SarvamCallImportRequest(
                attemptId,
                "connected",
                payload.UserName,
                payload.CallerPhoneNumber ?? payload.CallbackNumber,
                payload.CallSummary,
                payload.CallReason,
                [],
                ParseIndiaDateTime(payload.StartDateTime)?.UtcDateTime,
                null),
            cancellationToken);

        if (!result.Imported && !result.Duplicate)
        {
            return UnprocessableEntity(new { error = result.FailureReason });
        }

        return Ok(new { callId = result.CallId, imported = result.Imported, duplicate = result.Duplicate });
    }

    [HttpPost("call-ended")]
    public async Task<IActionResult> CallEnded(
        [FromQuery] string? key,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SarvamCallEndedPayload? payload,
        CancellationToken cancellationToken)
    {
        if (!SecretMatches(key))
        {
            return Unauthorized();
        }

        payload ??= new SarvamCallEndedPayload();
        var attemptId = FirstNonEmpty(payload.AttemptId, payload.InteractionId) ?? $"test:{DateTime.UtcNow:yyyyMMddHHmmss}";

        var variables = MergeVariables(payload.FinalAgentVariables, payload.OutputAgentVariables, payload.Extra);
        RememberInboundCaller(
            FirstNonEmpty(
                payload.UserPhoneNumber,
                FindCaller(payload.ChannelInfo, variables),
                ReadCallerFromRequest()),
            FindVariable(variables, "user_name", "caller_name", "customer_name", "patient_name", "name"));
        var result = await _managed.ImportCallAsync(
            new SarvamCallImportRequest(
                attemptId,
                payload.Status ?? "unknown",
                FindVariable(variables, "user_name", "caller_name", "customer_name", "patient_name", "name"),
                FirstNonEmpty(
                    payload.UserPhoneNumber,
                    FindCaller(payload.ChannelInfo, variables),
                    FindVariable(variables, "user_phone_number", "caller_phone_number", "customer_phone_number", "caller_phone", "phone"),
                    ReadJsonString(payload.Extra, "user_phone_number", "caller_phone_number", "customer_phone_number"),
                    ReadCallerFromRequest()),
                FirstNonEmpty(
                    FindVariable(variables, "call_summary", "summary", "call_outcome", "disposition"),
                    ReadJsonString(payload.Extra, "call_summary", "summary")),
                FindVariable(variables, "intent", "call_reason", "purpose"),
                payload.InteractionTranscript?
                    .Select(x => (x.Role ?? "unknown", x.Spoken))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Item2))
                    .ToArray() ?? [],
                null,
                payload.Duration,
                CombineFailure(payload)),
            cancellationToken);

        if (!result.Imported && !result.Duplicate)
        {
            return UnprocessableEntity(new { error = result.FailureReason });
        }

        _logger.LogInformation("Imported Sarvam Voicebot call {AttemptId} as {CallId}", payload.AttemptId, result.CallId);
        return Ok(new { callId = result.CallId, imported = result.Imported, duplicate = result.Duplicate });
    }

    private bool SecretMatches(string? querySecret)
    {
        var provided = !string.IsNullOrWhiteSpace(querySecret)
            ? querySecret
            : Request.Headers["X-Webhook-Secret"].FirstOrDefault();
        var configured = _config["SarvamManaged:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(provided) || string.IsNullOrWhiteSpace(configured))
        {
            return false;
        }

        var providedBytes = Encoding.UTF8.GetBytes(provided);
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        return providedBytes.Length == configuredBytes.Length
            && CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes);
    }

    private static string AppendBookingGuidance(string? instructions, ClinicRosterDto roster)
    {
        var guidance = $"""

            Appointment booking:
            {(roster.Doctors.Count == 1
                ? $"The only doctor is {roster.Doctors[0].DoctorName} ({roster.Doctors[0].Specialization}). Never say any other doctor name. Do not ask which doctor they want. After the problem, date, and time, book with {roster.Doctors[0].DoctorName}."
                : $"The ONLY clinic doctors in the database are: {roster.Roster}. Allowed names: {roster.AllowedNames}. Never say any other doctor name.")}
            Stay polite. Never hang up. Never invent names.
            Ask the problem first, then the date.
            Always call tool:check_anuj_availability first with date, problem, and doctor_name empty.
            Then say only doctors_to_say or spoken_prompt or available_doctor_names from that tool. If the tool result has no doctor name, say you are still checking. Do not guess.
            {(roster.Doctors.Count == 1
                ? $"needs_doctor_choice will be false. Offer the returned times and book with {roster.Doctors[0].DoctorName}."
                : "If needs_doctor_choice is true, ask who they want. Do not book yet. If they say anyone, book with doctor_name anyone.")}
            Confirm success only when booked is true. Then say the real doctor_name from the booking result.
            """;
        var existing = instructions ?? string.Empty;
        var cut = existing.IndexOf("Appointment booking:", StringComparison.OrdinalIgnoreCase);
        if (cut >= 0)
        {
            existing = existing[..cut].TrimEnd();
        }

        return existing + guidance;
    }

    private string? ReadForm(params string[] names)
    {
        if (!Request.HasFormContentType)
        {
            return null;
        }

        foreach (var name in names)
        {
            if (Request.Form.TryGetValue(name, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value.ToString();
            }
        }

        return null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

    private static bool TryParseLocalDate(string? value, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        var parsed = DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)
            || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var dateTime)
                && (date = DateOnly.FromDateTime(dateTime)) != default;
        if (parsed)
        {
            var currentYear = DateOnly.FromDateTime(IndiaTime.Now).Year;
            if (date.Year < currentYear)
            {
                date = new DateOnly(currentYear, date.Month, date.Day);
            }
        }

        return parsed;
    }

    private static bool TryParseLocalDateTime(string? value, out DateTime localStart)
    {
        localStart = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        if (DateTime.TryParseExact(
                text,
                ["yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm"],
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed)
            || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
        {
            localStart = DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified);
            var currentYear = IndiaTime.Now.Year;
            if (localStart.Year < currentYear)
            {
                localStart = new DateTime(currentYear, localStart.Month, localStart.Day, localStart.Hour, localStart.Minute, localStart.Second);
            }

            return true;
        }

        return false;
    }

    private static DateTimeOffset? ParseIndiaDateTime(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || !DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return null;
        }

        return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.FromHours(5.5));
    }

    private static string CreateStableAttemptId(string? phone, string? startedAt, string summary)
    {
        var eventTime = string.IsNullOrWhiteSpace(startedAt)
            ? DateTime.UtcNow.ToString("yyyyMMddHHmm", CultureInfo.InvariantCulture)
            : startedAt;
        var value = $"{phone}|{eventTime}|{summary}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return $"hook:{Convert.ToHexString(hash)[..32].ToLowerInvariant()}";
    }

    private static Dictionary<string, string> ToStringDictionary(Dictionary<string, JsonElement>? values) =>
        values?.ToDictionary(
            x => x.Key,
            x => x.Value.ValueKind == JsonValueKind.String ? x.Value.GetString() ?? string.Empty : x.Value.ToString(),
            StringComparer.OrdinalIgnoreCase)
        ?? new(StringComparer.OrdinalIgnoreCase);

    private static string? FindVariable(IReadOnlyDictionary<string, string> variables, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (variables.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return null;
    }

    private static Dictionary<string, string> MergeVariables(
        Dictionary<string, JsonElement>? finalVars,
        Dictionary<string, JsonElement>? outputVars,
        Dictionary<string, JsonElement>? extra)
    {
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var source in new[] { ToStringDictionary(finalVars), ToStringDictionary(outputVars), ToStringDictionary(extra) })
        {
            foreach (var pair in source)
            {
                if (!string.IsNullOrWhiteSpace(pair.Value))
                {
                    merged[pair.Key] = pair.Value;
                }
            }
        }

        return merged;
    }

    private static string? ReadJsonString(Dictionary<string, JsonElement>? extra, params string[] keys)
    {
        if (extra is null)
        {
            return null;
        }

        foreach (var key in keys)
        {
            if (extra.TryGetValue(key, out var value) && value.ValueKind == JsonValueKind.String)
            {
                var text = value.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }

        return null;
    }

    private static string? FindCaller(SarvamChannelInfo? channel, IReadOnlyDictionary<string, string> variables)
    {
        foreach (var key in new[]
        {
            "user_phone_number", "caller_phone_number", "customer_phone_number",
            "caller_number", "customer_number", "phone_number", "from"
        })
        {
            if (channel?.AdditionalFields is not null
                && channel.AdditionalFields.TryGetValue(key, out var channelValue)
                && channelValue.ValueKind == JsonValueKind.String)
            {
                var text = channelValue.GetString();
                if (!string.IsNullOrWhiteSpace(text) && !LooksLikeClinicNumber(text))
                {
                    return text;
                }
            }

            if (variables.TryGetValue(key, out var variableValue) && !LooksLikeClinicNumber(variableValue))
            {
                return variableValue;
            }
        }

        return null;
    }

    private static bool LooksLikeClinicNumber(string? value)
    {
        var digits = Regex.Replace(value ?? "", @"\D", "");
        return digits.EndsWith("8047283845", StringComparison.Ordinal);
    }

    private void RememberInboundCaller(string? phone = null, string? name = null) =>
        _inbound.Remember(FirstNonEmpty(phone, ReadCallerFromRequest()), name);

    private string? ReadCallerFromRequest()
    {
        foreach (var key in new[]
        {
            "user_phone_number", "caller_phone_number", "caller_phone", "customer_phone_number",
            "CallFrom", "From", "from", "caller", "phone"
        })
        {
            var query = Request.Query[key].ToString();
            if (!string.IsNullOrWhiteSpace(query) && !LooksLikeClinicNumber(query))
            {
                return query;
            }

            if (Request.HasFormContentType && Request.Form.TryGetValue(key, out var form) && !string.IsNullOrWhiteSpace(form) && !LooksLikeClinicNumber(form.ToString()))
            {
                return form.ToString();
            }
        }

        foreach (var header in new[] { "X-Caller-Phone", "X-User-Phone", "From" })
        {
            var value = Request.Headers[header].ToString();
            if (!string.IsNullOrWhiteSpace(value) && !LooksLikeClinicNumber(value))
            {
                return value;
            }
        }

        return _inbound.RecentPhone();
    }

    private static string? CombineFailure(SarvamCallEndedPayload payload)
    {
        var extras = new List<string>();
        if (!string.IsNullOrWhiteSpace(payload.FailureReason))
        {
            extras.Add(payload.FailureReason.Trim());
        }

        if (payload.Extra is not null)
        {
            foreach (var key in new[] { "transfer_status", "transfer_result", "forward_status", "hangup_cause", "disconnect_reason" })
            {
                if (payload.Extra.TryGetValue(key, out var value))
                {
                    extras.Add($"{key}={value}");
                }
            }
        }

        return extras.Count == 0 ? null : string.Join(" ", extras);
    }
}

public sealed class SarvamCallEndedPayload
{
    [JsonPropertyName("attempt_id")]
    public string? AttemptId { get; set; }

    [JsonPropertyName("status")]
    public string? Status { get; set; }

    [JsonPropertyName("channel_info")]
    public SarvamChannelInfo? ChannelInfo { get; set; }

    [JsonPropertyName("duration")]
    [JsonConverter(typeof(FlexibleNullableDoubleConverter))]
    public double? Duration { get; set; }

    [JsonPropertyName("interaction_id")]
    public string? InteractionId { get; set; }

    [JsonPropertyName("failure_reason")]
    public string? FailureReason { get; set; }

    [JsonPropertyName("user_phone_number")]
    public string? UserPhoneNumber { get; set; }

    [JsonPropertyName("final_agent_variables")]
    public Dictionary<string, JsonElement>? FinalAgentVariables { get; set; }

    [JsonPropertyName("output_agent_variables")]
    public Dictionary<string, JsonElement>? OutputAgentVariables { get; set; }

    [JsonPropertyName("interaction_transcript")]
    public List<SarvamTranscriptItem>? InteractionTranscript { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class SarvamAgentEndedPayload
{
    [JsonPropertyName("interaction_id")]
    public string? InteractionId { get; set; }

    [JsonPropertyName("call_summary")]
    public string? CallSummary { get; set; }

    [JsonPropertyName("user_name")]
    public string? UserName { get; set; }

    [JsonPropertyName("caller_phone_number")]
    public string? CallerPhoneNumber { get; set; }

    [JsonPropertyName("call_reason")]
    public string? CallReason { get; set; }

    [JsonPropertyName("callback_number")]
    public string? CallbackNumber { get; set; }

    [JsonPropertyName("start_datetime")]
    public string? StartDateTime { get; set; }
}

public sealed class SarvamAvailabilityRequest
{
    [JsonPropertyName("date")]
    public string? Date { get; set; }

    [JsonPropertyName("requested_date")]
    public string? RequestedDate { get; set; }

    [JsonPropertyName("duration_minutes")]
    [JsonConverter(typeof(FlexibleNullableIntConverter))]
    public int? DurationMinutes { get; set; }

    [JsonPropertyName("problem")]
    public string? Problem { get; set; }

    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("doctor_name")]
    public string? DoctorName { get; set; }

    [JsonPropertyName("selected_doctor")]
    public string? SelectedDoctor { get; set; }

    [JsonPropertyName("preferred_time")]
    public string? PreferredTime { get; set; }

    [JsonPropertyName("time")]
    public string? Time { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }

    [JsonIgnore]
    public string? ExtraDate => Extra is null
        ? null
        : Extra.TryGetValue("requested_date", out var requested) ? requested.ToString().Trim('"')
        : Extra.TryGetValue("calendar_date", out var calendar) ? calendar.ToString().Trim('"')
        : null;
}

public sealed class FlexibleNullableIntConverter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetInt32(out var number))
        {
            return number;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value.Value);
    }
}

public sealed class FlexibleNullableDoubleConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.Number && reader.TryGetDouble(out var number))
        {
            return number;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var text = reader.GetString();
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : null;
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteNumberValue(value.Value);
    }
}

public sealed class SarvamBookAppointmentRequest
{
    [JsonPropertyName("local_start")]
    public string? LocalStart { get; set; }

    [JsonPropertyName("selected_local_start")]
    public string? SelectedLocalStart { get; set; }

    [JsonPropertyName("caller_name")]
    public string? CallerName { get; set; }

    [JsonPropertyName("caller_phone")]
    public string? CallerPhone { get; set; }

    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

    [JsonPropertyName("appointment_purpose")]
    public string? AppointmentPurpose { get; set; }

    [JsonPropertyName("doctor_name")]
    public string? DoctorName { get; set; }

    [JsonPropertyName("selected_doctor")]
    public string? SelectedDoctor { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Extra { get; set; }
}

public sealed class SarvamChannelInfo
{
    [JsonPropertyName("agent_phone_number")]
    public string AgentPhoneNumber { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalFields { get; set; }
}

public sealed class SarvamTranscriptItem
{
    [JsonPropertyName("role")]
    public string? Role { get; set; }

    [JsonPropertyName("en_text")]
    public string? EnglishText { get; set; }

    [JsonPropertyName("english_text")]
    public string? EnglishTextAlt { get; set; }

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("indic_text")]
    public string? IndicText { get; set; }

    [JsonIgnore]
    public string Spoken =>
        new[] { EnglishText, EnglishTextAlt, Text }
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x) && !CallLogDetails.HasIndic(x))
            ?.Trim()
        ?? new[] { EnglishText, EnglishTextAlt, Text }
            .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x))
            ?.Trim()
        ?? "";
}
