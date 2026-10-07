using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Text.Json.Serialization;
using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.Models;
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
    private readonly IWebHostEnvironment _env;
    private readonly ILogger<SarvamManagedWebhookController> _logger;

    public SarvamManagedWebhookController(
        IConfiguration config,
        ISarvamManagedService managed,
        AppDbContext db,
        InboundCallerContext inbound,
        IWebHostEnvironment env,
        ILogger<SarvamManagedWebhookController> logger)
    {
        _config = config;
        _managed = managed;
        _db = db;
        _inbound = inbound;
        _env = env;
        _logger = logger;
    }

    [AcceptVerbs("GET", "POST")]
    [Route("agent-context")]
    [Route("agent-context/{key}")]
    public async Task<IActionResult> AgentContext(string? key, CancellationToken cancellationToken)
    {
        if (!SecretMatches(key))
        {
            return Unauthorized();
        }

        RememberInboundCaller();
        var caller = await _managed.GetKnownCallerAsync(
            FirstNonEmpty(ReadCallerFromRequest(), Request.Query["phone"].ToString()),
            cancellationToken);
        if (caller.Primary is not null)
        {
            _inbound.Remember(caller.Phone, caller.Primary.Name);
        }
        else
        {
            _inbound.Remember(caller.Phone);
        }
        var settings = await _db.AiSettings.FirstAsync(cancellationToken);
        var roster = await _managed.GetClinicRosterAsync(cancellationToken);
        var welcome = BuildSpokenWelcome(settings, caller);
        var description = BuildAgentDescription(settings);
        var instructions = BuildAgentInstructions(settings, roster, caller);
        return Ok(new
        {
            name = settings.AgentName,
            agent_name = settings.AgentName,
            agentName = settings.AgentName,
            agent_names = settings.AgentName,
            description,
            descriptions = description,
            agent_description = description,
            agent_descriptions = description,
            agentDescription = description,
            personality = description,
            personalitys = description,
            greeting = welcome,
            greetings = welcome,
            opening_message = welcome,
            welcome_message = welcome,
            welcome_messages = welcome,
            instructions,
            agent_instructions = instructions,
            agent_instruction = instructions,
            clinic_doctors = roster.Roster,
            doctor_roster = roster.Roster,
            allowed_doctor_names = roster.AllowedNames,
            doctors = roster.Doctors.Select(d => new { doctor_name = d.DoctorName, specialization = d.Specialization }),
            default_language = settings.Language.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "en-IN",
            supported_languages = settings.Language,
            voice = settings.VoiceSpeaker,
            transfer_number = settings.TransferNumber,
            caller_phone = caller.Phone,
            caller_phone_display = caller.DisplayPhone,
            used_web_test_number = caller.UsedWebTestNumber,
            returning_caller = caller.Returning,
            known_patient_names = caller.Patients.Select(p => p.Name).ToArray(),
            known_patients = caller.Patients.Select(p => new { patient_name = p.Name, upcoming = p.Upcoming ?? "none" })
        });
    }

    [AcceptVerbs("GET", "POST")]
    [Route("check-availability")]
    [Route("check-availability/{key}")]
    public async Task<IActionResult> CheckAvailability(
        string? key,
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
            Request.Query["day"].ToString(),
            Request.Query["when"].ToString(),
            ReadForm("date", "requested_date", "day", "when"),
            ReadExtraText(payload, "date", "requested_date", "appointment_date", "day", "when", "slot_date"));
        var rawTime = ClinicDateParser.CombineTimeHints(
            payload?.PreferredTime,
            payload?.Time,
            Request.Query["preferred_time"].ToString(),
            Request.Query["time"].ToString(),
            ReadForm("preferred_time", "time", "time_of_day"),
            ReadExtraText(payload, "preferred_time", "time", "time_of_day", "slot_time", "when"),
            ExtraTimeStrings(payload));

        if (!ClinicDateParser.TryResolve(rawDate, out var parsedDate, out var parsedTime))
        {
            parsedDate = DateOnly.FromDateTime(IndiaTime.Now);
        }

        rawTime = ClinicDateParser.CombineTimeHints(rawTime, parsedTime);
        _logger.LogInformation(
            "Availability request raw={Raw} parsed={Parsed} time={Time} preferred={Preferred} problem={Problem}",
            rawDate,
            parsedDate,
            rawTime,
            payload?.PreferredTime,
            FirstNonEmpty(payload?.Problem, payload?.Purpose));

        try
        {
            var result = await _managed.GetAvailabilityAsync(
                parsedDate,
                payload?.DurationMinutes,
                FirstNonEmpty(payload?.Problem, payload?.Purpose, payload?.Reason, Request.Query["problem"].ToString()),
                FirstNonEmpty(
                    payload?.DoctorName,
                    payload?.SelectedDoctor,
                    Request.Query["doctor_name"].ToString(),
                    ReadForm("doctor_name", "selected_doctor")),
                rawTime,
                cancellationToken);
            var names = result.Doctors.Select(d => d.DoctorName).ToArray();
            var doctorsToSay = names.Length == 0
                ? "none"
                : names.Length == 1
                    ? names[0]
                    : string.Join(" and ", names);
            var spoken = string.IsNullOrWhiteSpace(result.SpokenPrompt) ? result.Message : result.SpokenPrompt;
            var morningTimes = SpeakReturnedTimes(SlotsInHours(result.Slots, 9, 12));
            var afternoonTimes = SpeakReturnedTimes(SlotsInHours(result.Slots, 12, 16));
            var eveningTimes = SpeakReturnedTimes(SlotsInHours(result.Slots, 16, 24));
            var after12Times = FirstNonEmpty(afternoonTimes, eveningTimes) ?? "";
            var timesToSay = JoinWindows(morningTimes, afternoonTimes, eveningTimes);
            var window = ClinicDateParser.ParseTimeWindow(rawTime);
            var askTimeWindow = !window.Restricts
                || spoken.Contains("Do not read specific times yet", StringComparison.OrdinalIgnoreCase)
                || spoken.Contains("Ask only: morning, afternoon, or evening", StringComparison.OrdinalIgnoreCase);
            if (!askTimeWindow
                && !string.IsNullOrWhiteSpace(timesToSay)
                && !spoken.Contains(timesToSay, StringComparison.OrdinalIgnoreCase))
            {
                spoken = $"{spoken} Times: {timesToSay}.";
            }

            spoken = EnsureStayOnLine(spoken);
            var openWindows = new List<string>();
            if (!string.IsNullOrWhiteSpace(morningTimes)) openWindows.Add("morning");
            if (!string.IsNullOrWhiteSpace(afternoonTimes)) openWindows.Add("afternoon");
            if (!string.IsNullOrWhiteSpace(eveningTimes)) openWindows.Add("evening");
            var openWindowsToSay = openWindows.Count == 0
                ? "none"
                : openWindows.Count == 1
                    ? openWindows[0]
                    : openWindows.Count == 2
                        ? $"{openWindows[0]} or {openWindows[1]}"
                        : "morning, afternoon, and evening";
            _logger.LogInformation(
                "Availability {Date} window={Window} specialty={Specialty} doctors={Doctors} slots={Slots} morning={Morning} afternoon={Afternoon} evening={Evening}",
                parsedDate,
                window.Label,
                result.MatchedSpecialty,
                result.Doctors.Count,
                result.Slots.Count,
                morningTimes,
                afternoonTimes,
                eveningTimes);
            return Ok(new
            {
                value = spoken,
                text = spoken,
                times_to_say = askTimeWindow
                    ? "Ask morning, afternoon, or evening first. Do not read clock times yet."
                    : string.IsNullOrWhiteSpace(timesToSay) ? "none yet" : timesToSay,
                morning_times_to_say = askTimeWindow
                    ? "ask morning afternoon or evening first"
                    : string.IsNullOrWhiteSpace(morningTimes) ? "none" : morningTimes,
                afternoon_times_to_say = askTimeWindow
                    ? "ask morning afternoon or evening first"
                    : string.IsNullOrWhiteSpace(afternoonTimes) ? "none" : afternoonTimes,
                evening_times_to_say = askTimeWindow
                    ? "ask morning afternoon or evening first"
                    : string.IsNullOrWhiteSpace(eveningTimes) ? "none" : eveningTimes,
                ask_time_window = askTimeWindow,
                has_morning_slots = !string.IsNullOrWhiteSpace(morningTimes),
                has_afternoon_slots = !string.IsNullOrWhiteSpace(afternoonTimes),
                has_evening_slots = !string.IsNullOrWhiteSpace(eveningTimes),
                open_windows_to_say = openWindowsToSay,
                after_12_times_to_say = string.IsNullOrWhiteSpace(after12Times) ? "none" : after12Times,
                time_window = window.Label,
                doctors_to_say = doctorsToSay,
                available = result.Available,
                date = parsedDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                matched_specialty = result.MatchedSpecialty,
                needs_doctor_choice = result.NeedsDoctorChoice,
                doctor_count = result.Doctors.Count,
                available_doctor_names = names,
                spoken_prompt = spoken,
                keep_call_open = true,
                end_call = false,
                next_action = "Say spoken_prompt, then wait for the caller. Do not hang up.",
                doctors = result.Doctors.Select(d => new
                {
                    doctor_id = d.DoctorId,
                    doctor_name = d.DoctorName,
                    specialization = d.Specialization,
                    slot_count = d.SlotCount
                }),
                slots = result.Slots.Select(x => new { local_start = x.LocalStart, display = x.Display, doctor_name = x.DoctorName }),
                message = EnsureStayOnLine(result.Message)
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Availability check failed for {Date}", rawDate);
            return Ok(new
            {
                value = "The calendar could not be checked right now. Apologize, offer to check again, and do not end the call.",
                available = false,
                keep_call_open = true,
                end_call = false,
                doctors_to_say = "none",
                slots = Array.Empty<object>(),
                message = "The calendar could not be checked right now. Apologize and do not end the call."
            });
        }
    }

    [AcceptVerbs("GET", "POST")]
    [Route("book-appointment")]
    [Route("book-appointment/{key}")]
    public async Task<IActionResult> BookAppointment(
        string? key,
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
                keep_call_open = true,
                end_call = false,
                message = "Need a confirmed slot as yyyy-MM-ddTHH:mm:ss. Ask again and do not end the call."
            });
        }

        try
        {
            RememberInboundCaller();
            var callerPhone = CallerIdentity.ResolvePhone(
                payload.CallerPhone,
                ReadJsonString(payload.Extra, "user_phone_number", "caller_phone_number", "phone", "from"),
                ReadCallerFromRequest());
            var result = await _managed.BookAsync(
                localStart,
                FirstNonEmpty(payload.CallerName, _inbound.RecentNameFor(callerPhone)) ?? string.Empty,
                callerPhone,
                FirstNonEmpty(payload.Purpose, payload.AppointmentPurpose) ?? string.Empty,
                FirstNonEmpty(payload.DoctorName, payload.SelectedDoctor),
                cancellationToken);
            _logger.LogInformation("Book {Start} booked={Booked} id={Id} doctor={Doctor}", localStart, result.Booked, result.AppointmentId, result.DoctorName);
            var confirmation = result.Booked
                ? $"booked=true. Say exactly: {result.Message} Then ask if they need anything else. If they say no or goodbye, thank them, say goodbye, and end the call."
                : result.NeedsIdentity
                    ? $"booked=false. Say: {result.Message} Wait for the full name and number, then call book_anuj_appointment again. Do not say it is booked."
                    : $"booked=false. {result.Message} Do not say it is booked. Do not end the call.";
            return Ok(new
            {
                value = confirmation,
                booked = result.Booked,
                appointment_id = result.AppointmentId,
                local_start = result.LocalStart,
                doctor_name = result.DoctorName,
                patient_name = result.PatientName,
                caller_phone = callerPhone,
                needs_identity = result.NeedsIdentity,
                message = confirmation,
                confirmation,
                say_if_matched = string.IsNullOrWhiteSpace(result.PatientName)
                    ? ""
                    : $"Okay, booking will be created for {result.PatientName}.",
                say_if_booked = result.Booked ? "Your booking has been successfully created." : "",
                keep_call_open = !result.Booked,
                end_call = false,
                hang_up_after_goodbye = result.Booked,
                next_action = result.Booked
                    ? "Confirm the booking in the caller's language. Ask if they need anything else. After no or goodbye, say goodbye and end. Never end as uncertain."
                    : "Say the message and offer another time. Do not hang up.",
                alternative_slots = result.Alternatives.Select(x => new { local_start = x.LocalStart, display = x.Display })
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Appointment booking failed for {LocalStart}", payload.LocalStart);
            return Ok(new
            {
                booked = false,
                keep_call_open = true,
                end_call = false,
                message = "The appointment could not be booked right now. Offer to take a message and do not end the call."
            });
        }
    }

    [AcceptVerbs("GET", "POST")]
    [Route("queue-callback")]
    [Route("queue-callback/{key}")]
    public async Task<IActionResult> QueueCallback(
        string? key,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SarvamCallbackRequest? payload,
        CancellationToken cancellationToken)
    {
        if (!SecretMatches(key))
        {
            return Unauthorized();
        }

        RememberInboundCaller();
        var callerPhone = CallerIdentity.ResolvePhone(
            payload?.CallerPhone,
            ReadJsonString(payload?.Extra, "user_phone_number", "caller_phone_number", "phone", "from"),
            ReadCallerFromRequest());
        var callerName = FirstNonEmpty(
            payload?.CallerName,
            ReadJsonString(payload?.Extra, "caller_name", "user_name", "customer_name", "patient_name", "name"),
            Request.Query["caller_name"].ToString(),
            Request.Query["user_name"].ToString(),
            _inbound.RecentNameFor(callerPhone));
        var result = await _managed.QueueCallbackAsync(
            callerName ?? string.Empty,
            callerPhone,
            FirstNonEmpty(payload?.Reason, payload?.Summary, payload?.Purpose, callerName),
            cancellationToken);
        return Ok(new
        {
            value = result.Message,
            queued = result.Queued,
            callback_requested = true,
            caller_name = result.CallerName,
            caller_phone = result.CallerPhone,
            message = result.Message,
            keep_call_open = true,
            end_call = false
        });
    }

    [HttpPost("agent-ended")]
    [HttpPost("agent-ended/{key}")]
    public async Task<IActionResult> AgentEnded(
        string? key,
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
    [HttpPost("call-ended/{key}")]
    public async Task<IActionResult> CallEnded(
        string? key,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] SarvamCallEndedPayload? payload,
        CancellationToken cancellationToken)
    {
        if (!SecretMatches(key))
        {
            return Unauthorized();
        }

        payload ??= new SarvamCallEndedPayload();
        var attemptId = FirstNonEmpty(payload.AttemptId, payload.InteractionId)
            ?? CreateStableAttemptId(
                payload.UserPhoneNumber ?? payload.CallbackNumber,
                payload.StartDateTime,
                payload.CallSummary);
        var status = NormalizeEndedStatus(FirstNonEmpty(
            payload.Status,
            payload.CallStatus,
            ReadJsonString(payload.Extra, "status", "call_status", "end_status", "disposition")));

        var variables = MergeVariables(payload.FinalAgentVariables, payload.OutputAgentVariables, payload.Extra);
        var summary = FirstNonEmpty(
            FindVariable(variables, "call_summary", "summary", "call_outcome", "disposition"),
            ReadJsonString(payload.Extra, "call_summary", "summary"),
            payload.CallSummary);
        var callbackRequested = IsTruthy(FindVariable(
                variables,
                "callback_requested",
                "needs_callback",
                "request_callback",
                "callback_needed",
                "wants_callback"))
            || CallLogDetails.WantsCallback(summary ?? "")
            || CallLogDetails.WantsCallback(FindVariable(variables, "intent", "call_reason", "purpose") ?? "");
        if (callbackRequested && !CallLogDetails.WantsCallback(summary ?? ""))
        {
            summary = string.IsNullOrWhiteSpace(summary)
                ? "Caller requested a callback."
                : $"{summary.Trim()} Caller requested a callback.";
        }

        RememberInboundCaller(
            FirstNonEmpty(
                payload.UserPhoneNumber,
                FindCaller(payload.ChannelInfo, variables),
                ReadCallerFromRequest()),
            FindVariable(variables, "user_name", "caller_name", "customer_name", "patient_name", "name"));
        var result = await _managed.ImportCallAsync(
            new SarvamCallImportRequest(
                attemptId,
                status,
                FindVariable(variables, "user_name", "caller_name", "customer_name", "patient_name", "name"),
                FirstNonEmpty(
                    payload.UserPhoneNumber,
                    FindCaller(payload.ChannelInfo, variables),
                    FindVariable(variables, "user_phone_number", "caller_phone_number", "customer_phone_number", "caller_phone", "phone"),
                    ReadJsonString(payload.Extra, "user_phone_number", "caller_phone_number", "customer_phone_number"),
                    ReadCallerFromRequest()),
                summary,
                callbackRequested
                    ? "Callback"
                    : FindVariable(variables, "intent", "call_reason", "purpose"),
                payload.InteractionTranscript?
                    .Select(x => (x.Role ?? "unknown", x.Spoken))
                    .Where(x => !string.IsNullOrWhiteSpace(x.Item2))
                    .ToArray() ?? [],
                ParseIndiaDateTime(FirstNonEmpty(
                    payload.StartDateTime,
                    ReadJsonString(payload.Extra, "start_datetime", "started_at", "start_time", "call_start")))?.UtcDateTime,
                payload.Duration,
                CombineFailure(payload)),
            cancellationToken);

        if (!result.Imported && !result.Duplicate)
        {
            return UnprocessableEntity(new { error = result.FailureReason });
        }

        _logger.LogInformation("Imported Sarvam Voicebot call {AttemptId} as {CallId} status={Status}", attemptId, result.CallId, status);
        return Ok(new { callId = result.CallId, imported = result.Imported, duplicate = result.Duplicate });
    }

    private bool SecretMatches(string? querySecret)
    {
        var configured = _config["SarvamManaged:WebhookSecret"]?.Trim();
        if (string.IsNullOrWhiteSpace(configured))
        {
            _logger.LogWarning("Sarvam webhook unauthorized for {Path}: server secret is not configured", Request.Path);
            return false;
        }

        var candidates = new[]
        {
            querySecret,
            Request.RouteValues.TryGetValue("key", out var routeKey) ? routeKey?.ToString() : null,
            Request.Query["key"].ToString(),
            Request.Query["secret"].ToString(),
            Request.Headers["X-Webhook-Secret"].FirstOrDefault()
        };
        var configuredBytes = Encoding.UTF8.GetBytes(configured);
        foreach (var candidate in candidates.Where(value => !string.IsNullOrWhiteSpace(value)).Select(value => value!.Trim()).Distinct())
        {
            var providedBytes = Encoding.UTF8.GetBytes(candidate);
            if (providedBytes.Length == configuredBytes.Length
                && CryptographicOperations.FixedTimeEquals(providedBytes, configuredBytes))
            {
                return true;
            }
        }

        if (_env.IsDevelopment())
        {
            _logger.LogWarning("Allowing {Path} without a matching key in Development so the live call can continue", Request.Path);
            return true;
        }

        _logger.LogWarning(
            "Sarvam webhook unauthorized for {Path}: no matching key in query, path, or X-Webhook-Secret (query={HasQuery})",
            Request.Path,
            Request.QueryString.HasValue);
        return false;
    }

    private static string SpeakReturnedTimes(IReadOnlyList<AvailableSlotDto>? slots)
    {
        if (slots is null || slots.Count == 0)
        {
            return "";
        }

        var times = slots
            .Take(3)
            .Select(slot =>
            {
                if (ClinicDateParser.TryParseSlotLocal(slot.LocalStart, out var time))
                {
                    return time.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-IN"));
                }

                return slot.Display;
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return times.Count switch
        {
            0 => "",
            1 => times[0],
            2 => $"{times[0]} or {times[1]}",
            _ => $"{times[0]}, {times[1]}, or {times[2]}"
        };
    }

    private static List<AvailableSlotDto> SlotsInHours(IReadOnlyList<AvailableSlotDto> slots, int fromHour, int toHour) =>
        slots.Where(slot =>
            ClinicDateParser.TryParseSlotLocal(slot.LocalStart, out var time)
            && time.Hour >= fromHour
            && time.Hour < toHour).ToList();

    private static string JoinWindows(string morning, string afternoon, string evening)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(morning))
        {
            parts.Add($"morning 9 to 12 {morning}");
        }

        if (!string.IsNullOrWhiteSpace(afternoon))
        {
            parts.Add($"afternoon 12 to 4 {afternoon}");
        }

        if (!string.IsNullOrWhiteSpace(evening))
        {
            parts.Add($"evening after 4 {evening}");
        }

        return string.Join("; ", parts);
    }

    private static string EnsureStayOnLine(string? text)
    {
        var value = string.IsNullOrWhiteSpace(text) ? "I am still checking. Please stay on the line." : text.Trim();
        if (value.Contains("hang up", StringComparison.OrdinalIgnoreCase)
            || value.Contains("stay on the line", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return value + " Stay on the line. Never hang up.";
    }

    private static string BuildSpokenWelcome(AiSettings settings, KnownCallerDto caller)
    {
        var clinic = string.IsNullOrWhiteSpace(settings.WelcomeMessage)
            ? $"Hello, this is {settings.AgentName}. How can I help you today?"
            : settings.WelcomeMessage.Trim();
        var realNames = caller.Patients
            .Select(p => p.Name)
            .Where(CallerIdentity.HasRealName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (!caller.UsedWebTestNumber && realNames.Count == 1)
        {
            clinic = $"Welcome back, {realNames[0]}. {clinic}";
        }

        var consent = settings.ConsentMessage?.Trim() ?? "";
        return string.IsNullOrWhiteSpace(consent) ? clinic : $"{clinic} {consent}";
    }

    private static string BuildAgentDescription(AiSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.WelcomeMessage))
        {
            return settings.WelcomeMessage.Trim();
        }

        return $"{settings.AgentName} books Anuj Clinic appointments and answers short clinic questions.";
    }

    private static string BuildAgentInstructions(AiSettings settings, ClinicRosterDto roster, KnownCallerDto? caller)
    {
        return AppendBookingGuidance(settings.Instructions, settings.AgentName, roster, caller);
    }

    private static string AppendBookingGuidance(string? instructions, string agentName, ClinicRosterDto roster, KnownCallerDto? caller = null)
    {
        var callerBlock = caller is null
            ? ""
            : $"""

            Caller phone for this session is {caller.Phone} ({caller.DisplayPhone}). {(caller.UsedWebTestNumber ? "Sarvam web test has no live number, so use 7621806924." : "Use this live caller number.")}
            {caller.Guidance}
            Always send caller_phone={caller.Phone} on book_anuj_appointment.
            {(caller.Returning && caller.Primary is not null
                ? $"Greet them as a returning patient: {caller.WelcomeMessage}"
                : caller.Returning
                    ? "Ask which registered name is speaking before booking."
                    : "Ask their name before booking. If that name is new on this number, create a new patient.")}
            """;
        var guidance = $"""

            You are {agentName}, a live clinic receptionist. Be warm, short, and interactive. Ask one question at a time. Never hang up unless the caller clearly says goodbye.
            {(roster.Doctors.Count == 1
                ? $"The only doctor is {roster.Doctors[0].DoctorName}, {roster.Doctors[0].Specialization}. Never invent other doctor names."
                : $"Clinic doctors: {roster.Roster}. Allowed names: {roster.AllowedNames}. You may book any of these. If they have not named a doctor, ask who they want or send doctor_name=anyone. Never say there is only one doctor. Never invent names.")}
            {callerBlock}

            ALWAYS call check_anuj_availability as soon as they want an appointment, even if they did not give a date. If date is unknown, send date=tomorrow or earliest. Never skip the calendar tool. Never guess slots.

            Put the medical complaint in problem only. Put time of day in preferred_time as their words.
            First availability call: leave preferred_time empty. Ask only morning, afternoon, or evening. Do not read clock times yet.
            After they pick a window, call check_anuj_availability again with preferred_time=morning or afternoon or evening, then speak only that window's times.
            Morning / subah / सुबह = 9 to 12.
            Dupher / dopahar / दोपहर / afternoon = 12 to 4.
            Evening / shaam / शाम = after 4.
            Never say no slots available when has_evening_slots, has_afternoon_slots, or has_morning_slots is true, or when open_windows_to_say is not none.

            Conversation:
            1. Greet using welcome_message. If returning_caller is true, greet them by known_patient_names. If more than one name, ask who is speaking.
            2. When they say a name (for example Shubham), send that exact name as caller_name and the live caller_phone. Name plus phone is the unique patient id. Never reuse another caller's name or number.
            3. If the tool says the name and phone match, say: Okay, booking will be created for the full name it returns. Then create the booking on that profile. Never invent a surname.
            4. If the tool says no match or needs_identity is true, say: Can you please confirm your full name and number? Wait. Then book again with the full name and number they give. A new patient is created only after a full name is given.
            5. New appointment: call check_anuj_availability. If ask_time_window is true, ask only: morning, afternoon, or evening? Do not suggest specific times.
            6. After they choose morning, afternoon, or evening, call the availability tool again with that preferred_time. Then speak only those times. Never say no evening slots if has_evening_slots is true.
            7. When they pick a time, call book_anuj_appointment with local_start, caller_name, caller_phone={caller?.Phone ?? "the session caller_phone"}, purpose, doctor_name.
            8. Confirm only if booked is true. Say: Okay, booking will be created for the returned patient_name. Your booking has been successfully created. Then say the doctor, day, and time once. Then ask if they need anything else.
            9. If they say wait / stay / hold, stay on the line. If they say no or goodbye, thank them, repeat the confirmed time, say goodbye, and end the call. Never leave the call hanging. Never end as uncertain if a booking succeeded.
            10. Reschedule: check slots first, then book the new time.
            11. If they ask for a callback, call back, or to be phoned later: if you do not have their name yet, ask it first. Then call queue_anuj_callback with caller_name set to that real name and caller_phone. Never send Unknown. Then say someone from the clinic will call them back. Do not hang up.
            12. If speech is unclear, ask them to repeat. If the tool fails, apologize and retry. If no slots, offer the next day from the tool.

            Hang up only after an explicit goodbye, and only after you have clearly confirmed any booking.
            """;
        var existing = instructions ?? string.Empty;
        foreach (var marker in new[] { "a live clinic receptionist.", "You are a live clinic receptionist.", "Appointment booking" })
        {
            var cut = existing.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
            if (cut >= 0)
            {
                existing = existing[..cut].TrimEnd();
                break;
            }
        }

        return existing + guidance;
    }

    private static string? ReadExtraText(SarvamAvailabilityRequest? payload, params string[] keys)
    {
        if (payload?.Extra is null)
        {
            return payload?.ExtraDate;
        }

        foreach (var key in keys)
        {
            if (!payload.Extra.TryGetValue(key, out var value))
            {
                continue;
            }

            var text = value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString().Trim('"');
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text;
            }
        }

        return payload.ExtraDate;
    }

    private static string? ExtraTimeStrings(SarvamAvailabilityRequest? payload)
    {
        if (payload?.Extra is null)
        {
            return null;
        }

        var texts = payload.Extra
            .Where(pair =>
            {
                var key = pair.Key.ToLowerInvariant();
                return key.Contains("time") || key.Contains("when") || key.Contains("slot") || key.Contains("prefer") || key.Contains("after");
            })
            .Select(pair => pair.Value.ValueKind == JsonValueKind.String ? pair.Value.GetString() : pair.Value.ToString().Trim('"'))
            .Where(text => !string.IsNullOrWhiteSpace(text) && !ClinicDateParser.IsPlaceholderTime(text));
        return string.Join(" ", texts);
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

    private static bool IsTruthy(string? value)
    {
        var text = (value ?? "").Trim();
        return text.Equals("true", StringComparison.OrdinalIgnoreCase)
            || text.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || text.Equals("1", StringComparison.OrdinalIgnoreCase)
            || text.Equals("callback", StringComparison.OrdinalIgnoreCase)
            || text.Equals("requested", StringComparison.OrdinalIgnoreCase);
    }

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
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        var hasZone = text.EndsWith("Z", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(text, @"[+-]\d{2}:?\d{2}$");
        if (hasZone
            && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var zoned))
        {
            return zoned;
        }

        if (!DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed))
        {
            return null;
        }

        if (parsed.Kind == DateTimeKind.Utc)
        {
            return new DateTimeOffset(parsed, TimeSpan.Zero);
        }

        return new DateTimeOffset(DateTime.SpecifyKind(parsed, DateTimeKind.Unspecified), TimeSpan.FromHours(5.5));
    }

    private static string NormalizeEndedStatus(string? status)
    {
        var value = (status ?? "").Trim().ToLowerInvariant();
        if (value is "" or "unknown" or "uncertain" or "unclear" or "completed" or "complete"
            or "success" or "ok" or "ended" or "hangup" or "hang_up" or "disconnected")
        {
            return "connected";
        }

        return string.IsNullOrWhiteSpace(status) ? "connected" : status.Trim();
    }

    private static string CreateStableAttemptId(string? phone, string? startedAt, string? summary)
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
            if (!string.IsNullOrWhiteSpace(query) && !CallerIdentity.IsMissingPhone(query))
            {
                return query;
            }

            if (Request.HasFormContentType && Request.Form.TryGetValue(key, out var form) && !CallerIdentity.IsMissingPhone(form.ToString()))
            {
                return form.ToString();
            }
        }

        foreach (var header in new[] { "X-Caller-Phone", "X-User-Phone", "From" })
        {
            var value = Request.Headers[header].ToString();
            if (!string.IsNullOrWhiteSpace(value) && !CallerIdentity.IsMissingPhone(value))
            {
                return value;
            }
        }

        return null;
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

    [JsonPropertyName("call_status")]
    public string? CallStatus { get; set; }

    [JsonPropertyName("callback_number")]
    public string? CallbackNumber { get; set; }

    [JsonPropertyName("start_datetime")]
    public string? StartDateTime { get; set; }

    [JsonPropertyName("call_summary")]
    public string? CallSummary { get; set; }

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

public sealed class SarvamCallbackRequest
{
    [JsonPropertyName("caller_name")]
    public string? CallerName { get; set; }

    [JsonPropertyName("caller_phone")]
    public string? CallerPhone { get; set; }

    [JsonPropertyName("reason")]
    public string? Reason { get; set; }

    [JsonPropertyName("summary")]
    public string? Summary { get; set; }

    [JsonPropertyName("purpose")]
    public string? Purpose { get; set; }

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
