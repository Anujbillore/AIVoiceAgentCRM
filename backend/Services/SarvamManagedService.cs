using System.Globalization;
using System.Text.RegularExpressions;
using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Hubs;
using AiVoicePortal.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Services;

public record AvailableSlotDto(string LocalStart, string Display, int DoctorId, string DoctorName, string Specialization = "");

public record AvailableDoctorDto(int DoctorId, string DoctorName, string Specialization, int SlotCount);

public record AvailabilityResult(
    bool Available,
    string Message,
    string SpokenPrompt,
    string MatchedSpecialty,
    bool NeedsDoctorChoice,
    IReadOnlyList<AvailableDoctorDto> Doctors,
    IReadOnlyList<AvailableSlotDto> Slots);

public record SarvamBookResult(
    bool Booked,
    int? AppointmentId,
    string LocalStart,
    string? DoctorName,
    string Message,
    IReadOnlyList<AvailableSlotDto> Alternatives,
    string PatientName = "",
    bool NeedsIdentity = false);

public record SarvamCallImportRequest(
    string AttemptId,
    string Status,
    string? CallerName,
    string? CallerPhone,
    string? Summary,
    string? Intent,
    IReadOnlyList<(string Role, string Text)> Transcript,
    DateTime? StartedAt,
    double? DurationSeconds,
    string? FailureReason = null);

public record SarvamCallImportResult(int? CallId, bool Imported, bool Duplicate, string? FailureReason);

public record ClinicRosterDto(string Roster, string AllowedNames, IReadOnlyList<AvailableDoctorDto> Doctors);

public record KnownPatientDto(int Id, string Name, string? Upcoming);

public record KnownCallerDto(
    string Phone,
    string DisplayPhone,
    bool UsedWebTestNumber,
    bool Returning,
    IReadOnlyList<KnownPatientDto> Patients,
    KnownPatientDto? Primary,
    string WelcomeMessage,
    string Guidance);

public interface ISarvamManagedService
{
    Task<ClinicRosterDto> GetClinicRosterAsync(CancellationToken cancellationToken = default);
    Task<KnownCallerDto> GetKnownCallerAsync(string? phone, CancellationToken cancellationToken = default);
    Task<AvailabilityResult> GetAvailabilityAsync(DateOnly date, int? durationMinutes, string? problem, string? doctorName, string? preferredTime, CancellationToken cancellationToken = default);
    Task<SarvamBookResult> BookAsync(DateTime localStart, string callerName, string callerPhone, string purpose, string? doctorName, CancellationToken cancellationToken = default);
    Task<SarvamCallbackResult> QueueCallbackAsync(string callerName, string callerPhone, string? reason, CancellationToken cancellationToken = default);
    Task<SarvamCallImportResult> ImportCallAsync(SarvamCallImportRequest request, CancellationToken cancellationToken = default);
}

public record SarvamCallbackResult(bool Queued, string CallerName, string CallerPhone, string Message);

public class SarvamManagedService : ISarvamManagedService
{
    private readonly AppDbContext _db;
    private readonly IAppointmentService _appointments;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ISarvamAiService _ai;
    private readonly InboundCallerContext _inbound;

    public SarvamManagedService(AppDbContext db, IAppointmentService appointments, IHubContext<DashboardHub> hub, ISarvamAiService ai, InboundCallerContext inbound)
    {
        _db = db;
        _appointments = appointments;
        _hub = hub;
        _ai = ai;
        _inbound = inbound;
    }

    public async Task<ClinicRosterDto> GetClinicRosterAsync(CancellationToken cancellationToken = default)
    {
        var doctors = await GetBookableDoctorsAsync(cancellationToken);

        var lines = doctors.Select(d =>
        {
            var days = d.Schedules.Count == 0
                ? "no hours"
                : string.Join("/", d.Schedules.OrderBy(s => s.DayOfWeek).Select(s => s.DayOfWeek.ToString()[..3]));
            return $"{d.Name} ({d.Specialization}, {days})";
        }).ToList();

        return new ClinicRosterDto(
            lines.Count == 0 ? "No active doctors" : string.Join(". ", lines),
            doctors.Count == 0 ? "none" : string.Join(", ", doctors.Select(d => d.Name)),
            doctors.Select(d => new AvailableDoctorDto(d.Id, d.Name, d.Specialization, 0)).ToList());
    }

    public async Task<KnownCallerDto> GetKnownCallerAsync(string? phone, CancellationToken cancellationToken = default)
    {
        var resolved = CallerIdentity.ResolvePhone(phone);
        _inbound.Remember(resolved);
        var last10 = CallerIdentity.Last10(resolved);
        var patients = (await _db.Patients.ToListAsync(cancellationToken))
            .Where(p => CallerIdentity.Last10(p.Contact) == last10)
            .OrderBy(p => p.Id)
            .ToList();
        var now = IndiaTime.Now;
        var ids = patients.Select(p => p.Id).ToHashSet();
        var upcoming = await _db.Appointments
            .Include(a => a.Doctor)
            .Where(a => ids.Contains(a.PatientId) && a.Status != "Cancelled" && a.ScheduledAt >= now)
            .OrderBy(a => a.ScheduledAt)
            .ToListAsync(cancellationToken);
        var known = patients.Select(patient =>
        {
            var next = upcoming.FirstOrDefault(a => a.PatientId == patient.Id);
            var when = next is null
                ? null
                : $"{next.ScheduledAt:ddd d MMM, h:mm tt} IST with {next.Doctor.Name}";
            return new KnownPatientDto(patient.Id, patient.Name, when);
        }).ToList();

        var webTest = CallerIdentity.UsedWebTestFallback(resolved);
        var display = CallerIdentity.DisplayPhone(resolved);
        var primary = known.Count == 1 ? known[0] : null;
        var welcome = known.Count switch
        {
            0 => webTest
                ? "Hello, welcome to Anuj Clinic. How can I help you today?"
                : "Hello, welcome to Anuj Clinic. How can I help you today?",
            1 => string.IsNullOrWhiteSpace(primary?.Upcoming)
                ? $"Welcome back, {primary!.Name}. Nice to hear from you again. How can I help you today?"
                : $"Welcome back, {primary!.Name}. You already have an appointment {primary.Upcoming}. Ask if they want that visit, a new booking, or something else.",
            _ => $"Welcome back. This number is registered for {string.Join(" and ", known.Select(p => p.Name))}. Ask who is speaking, then help them."
        };
        var guidance = known.Count switch
        {
            0 => $"New caller on {display}. Ask their name before booking. Always send caller_phone={resolved} and caller_name.",
            1 => $"Returning patient {primary!.Name} on {display}. Greet them by name. If they book, send caller_name={primary.Name} and caller_phone={resolved}. Update this existing record; do not create a duplicate.",
            _ => $"Same number {display} has multiple patients: {string.Join(", ", known.Select(p => p.Name))}. Ask which name, then send that caller_name. If the name is new, create a new patient on this number."
        };
        if (webTest)
        {
            guidance += " Sarvam web test has no live caller number, so use 7621806924.";
        }

        return new KnownCallerDto(resolved, display, webTest, known.Count > 0, known, primary, welcome, guidance);
    }

    public async Task<AvailabilityResult> GetAvailabilityAsync(
        DateOnly date,
        int? durationMinutes,
        string? problem,
        string? doctorName,
        string? preferredTime,
        CancellationToken cancellationToken = default)
    {
        var bookable = await GetBookableDoctorsAsync(cancellationToken);
        var allSlots = await LoadSlotsAsync(date, durationMinutes, cancellationToken);
        var timeHint = ResolvePartOfDayHint(preferredTime, problem);
        if (bookable.Count == 1)
        {
            return await SoleDoctorOrNextAsync(bookable[0], timeHint, date, durationMinutes, cancellationToken);
        }

        var clinicSpecs = bookable.Select(d => d.Specialization).ToList();
        var specialty = MatchSpecialty(problem, clinicSpecs);
        var namedDoctor = NormalizeDoctorChoice(doctorName);
        if (IsAnyone(doctorName))
        {
            return OfferAnyoneAvailability(bookable, allSlots, timeHint, date);
        }

        if (string.IsNullOrWhiteSpace(namedDoctor))
        {
            var pool = bookable;
            if (!string.IsNullOrWhiteSpace(specialty))
            {
                var matched = bookable
                    .Where(d => DoctorSpecialties.Matches(d.Specialization, specialty))
                    .ToList();
                if (matched.Count == 1)
                {
                    return await SoleDoctorOrNextAsync(matched[0], timeHint, date, durationMinutes, cancellationToken);
                }

                if (matched.Count > 1)
                {
                    pool = matched;
                }
            }

            if (pool.Count > 1)
            {
                return AskWhichDoctor(pool, date, allSlots, timeHint);
            }
        }
        var kind = SpecialtyLabel(specialty);

        if (!string.IsNullOrWhiteSpace(namedDoctor))
        {
            var chosenDoctor = bookable.FirstOrDefault(d => DoctorNameMatches(d.Name, namedDoctor));
            if (chosenDoctor is not null)
            {
                return await SoleDoctorOrNextAsync(chosenDoctor, timeHint, date, durationMinutes, cancellationToken);
            }

            var others = FilterToSpecialty(allSlots, specialty);
            if (others.Count == 0)
            {
                others = FilterToSpecialty(allSlots, DoctorSpecialties.GeneralPhysician);
            }

            return DoctorChoiceResult(
                others.Count > 0,
                specialty,
                kind,
                SummarizeDoctors(others),
                namedDoctor);
        }

        var window = ClinicDateParser.ParseTimeWindow(timeHint);
        if (window.Restricts)
        {
            allSlots = allSlots
                .Where(s => ClinicDateParser.TryParseSlotLocal(s.LocalStart, out var slotTime)
                    && window.Matches(slotTime.TimeOfDay))
                .ToList();
        }

        var slots = FilterToSpecialty(allSlots, specialty);
        if (slots.Count == 0 && !string.Equals(specialty, DoctorSpecialties.GeneralPhysician, StringComparison.OrdinalIgnoreCase))
        {
            var gpSlots = FilterToSpecialty(allSlots, DoctorSpecialties.GeneralPhysician);
            if (gpSlots.Count > 0)
            {
                slots = gpSlots;
                specialty = DoctorSpecialties.GeneralPhysician;
                kind = SpecialtyLabel(specialty);
            }
        }

        var doctors = SummarizeDoctors(slots);
        if (doctors.Count > 1)
        {
            return AskWhichDoctor(bookable.Where(d => doctors.Any(x => x.DoctorId == d.Id)).ToList(), date, slots, timeHint);
        }

        if (doctors.Count == 1)
        {
            var only = bookable.FirstOrDefault(d => d.Id == doctors[0].DoctorId);
            if (only is not null)
            {
                return await SoleDoctorOrNextAsync(only, timeHint, date, durationMinutes, cancellationToken);
            }
        }

        var spoken = slots.Count > 0
            ? (window.Restricts
                ? $"Slots are available {SpeakDate(date)} {window.Label} at {SpeakSlotTimes(slots, 5)}. Ask which time they want. Stay on the line. Never hang up."
                : AskWindowsSpoken("A clinic doctor", date, slots))
            : $"No {kind} are available on that date. Offer another day. Do not end the call.";
        return new AvailabilityResult(
            slots.Count > 0,
            spoken,
            spoken,
            specialty,
            false,
            doctors,
            slots);
    }

    public async Task<SarvamBookResult> BookAsync(
        DateTime localStart,
        string callerName,
        string callerPhone,
        string purpose,
        string? doctorName,
        CancellationToken cancellationToken = default)
    {
        localStart = DateTime.SpecifyKind(IndiaTime.ToIstLocal(localStart), DateTimeKind.Unspecified);
        callerPhone = CallerIdentity.ResolvePhone(callerPhone);
        if (!CallerIdentity.HasRealName(callerName))
        {
            callerName = _inbound.RecentNameFor(callerPhone) ?? callerName;
        }

        _inbound.Remember(callerPhone, callerName);
        callerName = await _ai.TranslateToEnglishAsync(callerName, cancellationToken);
        purpose = await _ai.TranslateToEnglishAsync(purpose, cancellationToken);
        var identity = await ResolveBookingPatientAsync(callerName, callerPhone, cancellationToken);
        if (identity.Patient is null)
        {
            return new SarvamBookResult(false, null, FormatLocal(localStart), null, identity.Ask, [], NeedsIdentity: true);
        }

        var patient = identity.Patient;
        var anyone = IsAnyone(doctorName);
        doctorName = anyone ? null : NormalizeDoctorChoice(doctorName);
        var doctors = await GetBookableDoctorsAsync(cancellationToken);
        var specialty = MatchSpecialty(purpose, doctors.Select(d => d.Specialization));
        var allSlots = await LoadSlotsAsync(DateOnly.FromDateTime(localStart), 30, cancellationToken);
        var slots = FilterToSpecialty(allSlots, specialty);
        if (slots.Count == 0)
        {
            slots = FilterToSpecialty(allSlots, DoctorSpecialties.GeneralPhysician);
            if (slots.Count > 0)
            {
                specialty = DoctorSpecialties.GeneralPhysician;
            }
        }

        Doctor? doctor = null;
        if (doctors.Count == 1)
        {
            doctor = doctors[0];
            slots = allSlots.Where(s => s.DoctorId == doctor.Id).ToList();
        }
        else if (!anyone && !string.IsNullOrWhiteSpace(doctorName))
        {
            doctor = doctors.FirstOrDefault(d => DoctorNameMatches(d.Name, doctorName));
            if (doctor is null)
            {
                var availableNames = string.Join(", ", SummarizeDoctors(slots).Select(d => d.DoctorName));
                return new SarvamBookResult(
                    false,
                    null,
                    FormatLocal(localStart),
                    null,
                    $"{doctorName} is not a clinic doctor. Available: {(string.IsNullOrWhiteSpace(availableNames) ? "none that day" : availableNames)}. Ask who they want.",
                    slots.Take(3).ToList());
            }

            slots = allSlots.Where(s => s.DoctorId == doctor.Id).ToList();
        }

        var match = slots.FirstOrDefault(s =>
            DateTime.TryParse(s.LocalStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var slotTime)
            && slotTime == localStart);
        doctor ??= match is not null ? doctors.FirstOrDefault(d => d.Id == match.DoctorId) : null;

        if (doctor is null)
        {
            var availableNames = string.Join(", ", SummarizeDoctors(slots).Select(d => d.DoctorName));
            return new SarvamBookResult(
                false,
                null,
                FormatLocal(localStart),
                null,
                $"That time is not free. Available: {(string.IsNullOrWhiteSpace(availableNames) ? "none that day" : availableNames)}. Do not end the call.",
                slots.Take(3).ToList());
        }

        var already = await _db.Appointments
            .Include(a => a.Doctor)
            .Where(a => a.PatientId == patient.Id && a.Status != "Cancelled" && a.ScheduledAt == localStart)
            .OrderBy(a => a.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (already is not null)
        {
            return new SarvamBookResult(
                true,
                already.Id,
                FormatLocal(already.ScheduledAt),
                already.Doctor.Name,
                BookingSuccessMessage(patient.Name, already.Doctor.Name, already.ScheduledAt),
                [],
                patient.Name);
        }

        var notes = string.IsNullOrWhiteSpace(purpose)
            ? "Booked via Sarvam Voicebot"
            : $"Booked via Sarvam Voicebot: {purpose.Trim()}";

        try
        {
            var booked = await _appointments.BookAsync(
                new BookAppointmentRequest(patient.Id, doctor.Id, localStart, notes),
                pushDashboard: false,
                cancellationToken);
            await UpsertBookingCallLogAsync(patient, booked, cancellationToken);
            return new SarvamBookResult(
                true,
                booked.Id,
                FormatLocal(booked.ScheduledAt),
                booked.DoctorName,
                BookingSuccessMessage(booked.PatientName, booked.DoctorName, booked.ScheduledAt),
                [],
                booked.PatientName);
        }
        catch (InvalidOperationException ex)
        {
            var alternatives = await LoadSlotsAsync(DateOnly.FromDateTime(localStart), 30, cancellationToken);
            return new SarvamBookResult(false, null, FormatLocal(localStart), null, ex.Message + " Do not end the call.", alternatives.Take(3).ToList());
        }
    }

    public async Task<SarvamCallbackResult> QueueCallbackAsync(
        string callerName,
        string callerPhone,
        string? reason,
        CancellationToken cancellationToken = default)
    {
        callerPhone = CallerIdentity.ResolvePhone(callerPhone);
        var last10 = CallLogDetails.Last10(callerPhone);
        var since = DateTime.UtcNow.AddMinutes(-30);
        var recent = await _db.CallLogs
            .Where(c => c.Timestamp >= since)
            .OrderByDescending(c => c.Timestamp)
            .ToListAsync(cancellationToken);
        var callLog = last10.Length >= 10
            ? recent.FirstOrDefault(c =>
                CallerIdentity.PhonesMatch(c.CallerPhone, callerPhone)
                && !CallLogDetails.LooksBooked(c)
                && (CallerIdentity.SamePerson(c.CallerName, c.CallerPhone, callerName, callerPhone)
                    || !CallerIdentity.HasRealName(c.CallerName)
                    || !CallerIdentity.HasRealName(callerName))
                && ((c.Intent ?? "").Equals("Callback", StringComparison.OrdinalIgnoreCase)
                    || (c.Outcome ?? "").Equals("Callback", StringComparison.OrdinalIgnoreCase)
                    || (c.ExternalId ?? "").StartsWith("sarvam-callback:", StringComparison.OrdinalIgnoreCase)
                    || CallLogDetails.IsGenericSummary(c.Summary)))
            : null;
        callerName = await ResolveSpokenNameAsync(
            callerName,
            callerPhone,
            $"{reason} {callLog?.CallerName} {callLog?.Summary} {callLog?.Transcript}",
            callLog,
            cancellationToken);
        _inbound.Remember(callerPhone, callerName);
        var note = string.IsNullOrWhiteSpace(reason) ? "Caller requested a callback." : reason.Trim();
        if (callLog is null)
        {
            callLog = new CallLog
            {
                ExternalId = $"sarvam-callback:{(last10.Length >= 10 ? last10 : Guid.NewGuid().ToString("N")[..10])}",
                CallerName = callerName,
                CallerPhone = callerPhone,
                Summary = note.StartsWith("Caller requested", StringComparison.OrdinalIgnoreCase) ? note : $"Caller requested a callback. {note}",
                ActionTaken = "Callback requested",
                Intent = "Callback",
                Transcript = note,
                Timestamp = DateTime.UtcNow,
                Outcome = "Callback",
                TransferType = "Callback"
            };
            _db.CallLogs.Add(callLog);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else
        {
            if (CallerIdentity.HasRealName(callerName))
            {
                callLog.CallerName = callerName;
            }

            MarkCallbackRequested(callLog, note);
        }

        var callback = await EnsureImportedCallbackAsync(callLog, note, cancellationToken);
        if (CallerIdentity.HasRealName(callerName))
        {
            callback.CallerName = callerName;
        }
        await _db.SaveChangesAsync(cancellationToken);
        await _hub.Clients.All.SendAsync("CallSummaryAdded", CallLogDetails.ToDto(callLog, null, callback), cancellationToken);
        return new SarvamCallbackResult(
            true,
            CallLogDetails.DisplayName(callLog.CallerName),
            CallLogDetails.DisplayPhone(callLog.CallerPhone),
            "Callback queued. Say: I have noted your callback request. Someone from the clinic will call you back. Then ask if they need anything else.");
    }

    public async Task<SarvamCallImportResult> ImportCallAsync(
        SarvamCallImportRequest request,
        CancellationToken cancellationToken = default)
    {
        var attemptId = request.AttemptId.Trim();
        if (string.IsNullOrWhiteSpace(attemptId))
        {
            return new SarvamCallImportResult(null, false, false, "attempt_id is required");
        }

        var externalId = $"sarvam:{attemptId}";
        var callerPhone = CallerIdentity.ResolvePhone(request.CallerPhone);
        var callerName = string.IsNullOrWhiteSpace(request.CallerName) || request.CallerName.Contains("identifier", StringComparison.OrdinalIgnoreCase)
            ? "Unknown"
            : request.CallerName.Trim();
        var transcript = string.Join(" | ", request.Transcript
            .Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => $"{MapRole(t.Role)}: {t.Text.Trim()}"));
        if (CallerIdentity.IsMissingPhone(request.CallerPhone))
        {
            var cached = CallerIdentity.ResolvePhone(CallLogDetails.ExtractPhone(transcript));
            callerPhone = cached;
        }

        var summary = string.IsNullOrWhiteSpace(request.Summary)
            ? (string.IsNullOrWhiteSpace(transcript) ? "Sarvam Voicebot inbound call" : Truncate(transcript, 280))
            : Truncate(request.Summary.Trim(), 2000);
        var existing = await FindExistingCallAsync(externalId, callerPhone, callerName, cancellationToken);
        callerName = await ResolveSpokenNameAsync(
            callerName,
            callerPhone,
            $"{request.Summary} {transcript} {request.Intent}",
            existing,
            cancellationToken);
        _inbound.Remember(callerPhone, callerName);
        if (existing is not null)
        {
            if (CallerIdentity.HasRealName(callerName)
                && !CallerIdentity.HasRealName(existing.CallerName))
            {
                existing.CallerName = callerName;
            }

            MergeCall(
                existing,
                callerName,
                callerPhone,
                summary,
                transcript,
                request.Status,
                request.FailureReason,
                request.DurationSeconds,
                request.StartedAt is null ? null : ResolveCallTimestamp(request.StartedAt));
            var existingRelated = await FindRelatedAppointmentAsync(existing, cancellationToken);
            if (existingRelated is not null)
            {
                ApplyBookingToCall(existing, existingRelated);
            }

            await _ai.EnsureEnglishAsync(existing, cancellationToken);
            CallCallback? mergedCallback = null;
            if (CallLogDetails.IsCallbackRequested(existing, existingRelated, null))
            {
                MarkCallbackRequested(existing, existing.Summary);
                mergedCallback = await EnsureImportedCallbackAsync(existing, existing.Summary, cancellationToken);
            }

            await _db.SaveChangesAsync(cancellationToken);
            await _hub.Clients.All.SendAsync("CallSummaryAdded", CallLogDetails.ToDto(existing, existingRelated, mergedCallback), cancellationToken);
            return new SarvamCallImportResult(existing.Id, true, true, null);
        }
        var intent = string.IsNullOrWhiteSpace(request.Intent)
            ? GuessIntent($"{request.Status} {request.FailureReason} {summary} {transcript}")
            : request.Intent.Trim();
        var classifyText = $"{request.Status} {request.FailureReason} {summary} {transcript} {intent}";
        Patient? patient = null;
        if (CallerIdentity.HasRealName(callerName))
        {
            patient = await FindOrCreatePatientAsync(callerName, callerPhone, cancellationToken);
        }
        else
        {
            var onPhone = await PatientsOnPhoneAsync(callerPhone, cancellationToken);
            var named = onPhone.Where(p => CallerIdentity.HasRealName(p.Name)).ToList();
            if (named.Count == 1)
            {
                patient = named[0];
                callerName = named[0].Name;
            }
        }

        var timestamp = ResolveCallTimestamp(request.StartedAt);
        var durationSeconds = NormalizeDurationSeconds(request.DurationSeconds);
        var duration = durationSeconds is > 0
            ? $" ({Math.Round(durationSeconds.Value)}s)"
            : "";
        var forwarded = CallLogDetails.LooksForwarded(classifyText)
            || intent.Equals("Human", StringComparison.OrdinalIgnoreCase);
        var unanswered = CallLogDetails.LooksUnanswered(classifyText)
            || request.Status.Equals("failed", StringComparison.OrdinalIgnoreCase)
            || (request.FailureReason ?? "").Contains("no_answer", StringComparison.OrdinalIgnoreCase)
            || (request.FailureReason ?? "").Contains("unanswered", StringComparison.OrdinalIgnoreCase);

        var callLog = new CallLog
        {
            ExternalId = externalId,
            CallerName = patient?.Name ?? callerName,
            CallerPhone = callerPhone,
            Summary = summary,
            ActionTaken = request.Status.Equals("connected", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(request.Summary)
                ? $"Sarvam Voicebot completed{duration}"
                : $"Sarvam Voicebot {request.Status}{duration}",
            Intent = intent,
            Transcript = string.IsNullOrWhiteSpace(transcript) ? summary : transcript,
            Timestamp = timestamp,
            Outcome = forwarded ? "Forwarded" : request.Status.Equals("failed", StringComparison.OrdinalIgnoreCase) ? "Failed" : "Contained",
            EscalationReason = forwarded ? (string.IsNullOrWhiteSpace(request.FailureReason) ? "Call forwarded to clinic staff" : request.FailureReason.Trim()) : request.FailureReason ?? "",
            TransferType = forwarded ? (unanswered ? "Callback" : "Warm") : "None",
            PatientId = patient?.Id,
            DurationSeconds = durationSeconds ?? 0
        };

        _db.CallLogs.Add(callLog);
        await _db.SaveChangesAsync(cancellationToken);

        var related = await FindRelatedAppointmentAsync(callLog, cancellationToken);
        CallCallback? callback = null;
        var wantsCallback = CallLogDetails.WantsCallback(classifyText)
            || intent.Equals("Callback", StringComparison.OrdinalIgnoreCase);
        if (related is not null && !wantsCallback)
        {
            ApplyBookingToCall(callLog, related);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else if (forwarded && !wantsCallback)
        {
            callLog.Intent = "Human";
            if (unanswered)
            {
                callLog.ActionTaken = "Forwarded — not answered, callback required";
                callback = await QueueImportedCallbackAsync(
                    callLog,
                    "Call forwarded but not answered",
                    cancellationToken);
            }
            else
            {
                callLog.ActionTaken = "Forwarded to clinic staff";
                await _db.SaveChangesAsync(cancellationToken);
            }

            await _hub.Clients.All.SendAsync(
                "TransferRequested",
                new { callerName = callLog.CallerName, reason = callLog.ActionTaken },
                cancellationToken);
        }
        if (callback is null && wantsCallback)
        {
            MarkCallbackRequested(callLog, "Caller requested a callback.");
            callback = await EnsureImportedCallbackAsync(
                callLog,
                "Caller requested a callback",
                cancellationToken);
        }

        await _ai.EnsureEnglishAsync(callLog, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        var dto = CallLogDetails.ToDto(callLog, related, callback);
        await _hub.Clients.All.SendAsync("CallSummaryAdded", dto, cancellationToken);
        return new SarvamCallImportResult(callLog.Id, true, false, null);
    }

    private async Task<Appointment?> FindRelatedAppointmentAsync(CallLog callLog, CancellationToken cancellationToken)
    {
        var from = callLog.Timestamp.AddHours(-6);
        var to = callLog.Timestamp.AddHours(6);
        var candidates = await _db.Appointments
            .Include(a => a.Doctor)
            .Include(a => a.Patient)
            .Where(a => a.Status != "Cancelled" && a.CreatedAt >= from && a.CreatedAt <= to)
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        if (candidates.Count == 0 && callLog.PatientId is int patientId)
        {
            candidates = await _db.Appointments
                .Include(a => a.Doctor)
                .Include(a => a.Patient)
                .Where(a => a.Status != "Cancelled" && a.PatientId == patientId)
                .OrderByDescending(a => a.CreatedAt)
                .Take(5)
                .ToListAsync(cancellationToken);
        }

        return CallLogDetails.FindAppointment(callLog, candidates);
    }

    private async Task UpsertBookingCallLogAsync(Patient patient, AppointmentDto booked, CancellationToken cancellationToken)
    {
        var phone = CallLogDetails.DisplayPhone(NormalizePhone(patient.Contact));
        var existing = await FindExistingCallAsync($"sarvam-book:{booked.Id}", phone, patient.Name, cancellationToken);
        var callLog = existing ?? new CallLog
        {
            ExternalId = $"sarvam-book:{booked.Id}",
            Timestamp = DateTime.UtcNow,
            Intent = "Appointment",
            Outcome = "Contained"
        };
        await _ai.EnsureEnglishAsync(patient, cancellationToken);
        callLog.CallerName = patient.Name;
        callLog.CallerPhone = CallerIdentity.IsMissingPhone(phone) ? CallerIdentity.WebTestPhone : phone;
        callLog.PatientId = patient.Id;
        callLog.Summary = $"{patient.Name} booked with {booked.DoctorName} for {booked.ScheduledAt:ddd d MMM, h:mm tt} IST.";
        callLog.ActionTaken = $"Booked with {booked.DoctorName} at {booked.ScheduledAt:h:mm tt} IST";
        callLog.Transcript = string.IsNullOrWhiteSpace(callLog.Transcript) ? callLog.Summary : callLog.Transcript;
        if (existing is null)
        {
            _db.CallLogs.Add(callLog);
        }

        await _ai.EnsureEnglishAsync(callLog, cancellationToken);
        var appointment = await _db.Appointments.Include(a => a.Doctor).Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.Id == booked.Id, cancellationToken);
        if (appointment is not null)
        {
            await _ai.EnsureEnglishAsync(appointment, cancellationToken);
        }

        await _db.SaveChangesAsync(cancellationToken);
        await _hub.Clients.All.SendAsync("CallSummaryAdded", CallLogDetails.ToDto(callLog, appointment), cancellationToken);
    }

    private async Task<CallLog?> FindExistingCallAsync(string externalId, string phone, string? name, CancellationToken cancellationToken)
    {
        var byId = await _db.CallLogs.FirstOrDefaultAsync(c => c.ExternalId == externalId, cancellationToken);
        if (byId is not null)
        {
            return byId;
        }

        var last10 = CallLogDetails.Last10(phone);
        if (last10.Length < 10)
        {
            return null;
        }

        var since = DateTime.UtcNow.AddMinutes(-20);
        var matches = await _db.CallLogs
            .Where(c => c.Timestamp >= since)
            .OrderByDescending(c => c.Timestamp)
            .ToListAsync(cancellationToken);
        var samePhone = matches.Where(c => CallLogDetails.Last10(c.CallerPhone) == last10).ToList();
        if (samePhone.Count == 0)
        {
            return null;
        }

        if (CallerIdentity.HasRealName(name))
        {
            samePhone = samePhone
                .Where(c => !CallerIdentity.HasRealName(c.CallerName) || CallerIdentity.NamesMatch(c.CallerName, name))
                .ToList();
        }

        if (externalId.StartsWith("sarvam-book:", StringComparison.OrdinalIgnoreCase))
        {
            return samePhone.FirstOrDefault(c =>
                (c.ExternalId ?? "").StartsWith("sarvam:", StringComparison.OrdinalIgnoreCase)
                || (c.ExternalId ?? "").StartsWith("hook:", StringComparison.OrdinalIgnoreCase));
        }

        return samePhone.FirstOrDefault(c =>
            (c.ExternalId ?? "").StartsWith("sarvam-book:", StringComparison.OrdinalIgnoreCase)
            || (c.ExternalId ?? "").StartsWith("sarvam-callback:", StringComparison.OrdinalIgnoreCase)
            || (c.Intent ?? "").Equals("Callback", StringComparison.OrdinalIgnoreCase)
            || (c.Outcome ?? "").Equals("Callback", StringComparison.OrdinalIgnoreCase));
    }

    private static void MergeCall(CallLog existing, string callerName, string callerPhone, string summary, string transcript, string status, string? failureReason, double? durationSeconds, DateTime? startedAt)
    {
        if (startedAt is DateTime when && when != default)
        {
            existing.Timestamp = when;
        }

        if (existing.CallerName is "Unknown" or "Unknown caller" && callerName is not "Unknown")
        {
            existing.CallerName = callerName;
        }

        if (string.IsNullOrWhiteSpace(existing.CallerPhone) && !string.IsNullOrWhiteSpace(callerPhone))
        {
            existing.CallerPhone = callerPhone;
        }

        if (!string.IsNullOrWhiteSpace(transcript) && transcript.Length > (existing.Transcript?.Length ?? 0))
        {
            existing.Transcript = transcript;
        }

        if (CallLogDetails.IsGenericSummary(existing.Summary) && !CallLogDetails.IsGenericSummary(summary))
        {
            existing.Summary = summary;
        }

        if (!string.IsNullOrWhiteSpace(failureReason) && string.IsNullOrWhiteSpace(existing.EscalationReason))
        {
            existing.EscalationReason = failureReason;
        }

        var seconds = NormalizeDurationSeconds(durationSeconds);
        if (seconds is > 0)
        {
            existing.DurationSeconds = seconds.Value;
        }

        if (CallLogDetails.LooksBooked(existing))
        {
            return;
        }

        if (existing.ActionTaken.StartsWith("Sarvam Voicebot", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(status)
            && !status.Equals("unknown", StringComparison.OrdinalIgnoreCase)
            && !status.Equals("uncertain", StringComparison.OrdinalIgnoreCase))
        {
            var suffix = existing.DurationSeconds > 0 ? $" ({Math.Round(existing.DurationSeconds)}s)" : "";
            existing.ActionTaken = $"Sarvam Voicebot {status}{suffix}";
        }
    }

    private static double? NormalizeDurationSeconds(double? value)
    {
        if (value is null or <= 0)
        {
            return null;
        }

        return value > 10_000 ? value / 1000.0 : value;
    }

    private static void ApplyBookingToCall(CallLog callLog, Appointment related)
    {
        callLog.Outcome = "Contained";
        callLog.TransferType = "None";
        callLog.Intent = "Appointment";
        callLog.PatientId ??= related.PatientId;
        if (callLog.CallerName is "Unknown" or "Unknown caller" && !string.IsNullOrWhiteSpace(related.Patient?.Name))
        {
            callLog.CallerName = related.Patient.Name;
        }

        if (string.IsNullOrWhiteSpace(callLog.CallerPhone) && !string.IsNullOrWhiteSpace(related.Patient?.Contact))
        {
            callLog.CallerPhone = CallLogDetails.DisplayPhone(related.Patient.Contact);
        }

        callLog.ActionTaken = $"Booked with {related.Doctor.Name} at {related.ScheduledAt:h:mm tt} IST";
        if (CallLogDetails.IsGenericSummary(callLog.Summary))
        {
            callLog.Summary = $"{callLog.CallerName} booked with {related.Doctor.Name} for {related.ScheduledAt:ddd d MMM, h:mm tt} IST.";
        }
    }

    private async Task<List<AvailableSlotDto>> LoadSlotsAsync(
        DateOnly date,
        int? durationMinutes,
        CancellationToken cancellationToken)
    {
        var minutes = durationMinutes is > 0 and <= 180 ? durationMinutes.Value : 30;
        var doctors = await GetBookableDoctorsAsync(cancellationToken);

        var dayStart = date.ToDateTime(TimeOnly.MinValue);
        var dayEnd = dayStart.AddDays(1);
        var booked = await _db.Appointments
            .Where(a => a.Status != "Cancelled" && a.ScheduledAt >= dayStart && a.ScheduledAt < dayEnd)
            .Select(a => new { a.DoctorId, a.ScheduledAt })
            .ToListAsync(cancellationToken);
        var taken = booked
            .Select(a => (a.DoctorId, Tick: a.ScheduledAt.Ticks))
            .ToHashSet();

        var now = IndiaTime.Now;
        var earliest = date == DateOnly.FromDateTime(now)
            ? now.AddMinutes(30)
            : dayStart;

        var slots = new List<AvailableSlotDto>();
        foreach (var doctor in doctors)
        {
            foreach (var schedule in SchedulesOrDefault(doctor).Where(s => s.DayOfWeek == date.DayOfWeek))
            {
                var cursor = date.ToDateTime(TimeOnly.FromTimeSpan(schedule.StartTime));
                var end = date.ToDateTime(TimeOnly.FromTimeSpan(schedule.EndTime));
                while (cursor.AddMinutes(minutes) <= end)
                {
                    if (cursor >= earliest && !taken.Contains((doctor.Id, cursor.Ticks)))
                    {
                        slots.Add(new AvailableSlotDto(
                            cursor.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture),
                            $"{cursor:h:mm tt} IST with {doctor.Name}",
                            doctor.Id,
                            doctor.Name,
                            doctor.Specialization));
                    }

                    cursor = cursor.AddMinutes(minutes);
                }
            }
        }

        return slots
            .OrderBy(s => s.LocalStart)
            .ThenBy(s => s.DoctorName)
            .ToList();
    }

    private async Task<List<Doctor>> GetBookableDoctorsAsync(CancellationToken cancellationToken)
    {
        return await _db.Doctors
            .Include(d => d.Schedules)
            .Where(d => d.IsActive)
            .OrderBy(d => d.Name)
            .ToListAsync(cancellationToken);
    }

    private static List<DoctorSchedule> SchedulesOrDefault(Doctor doctor)
    {
        if (doctor.Schedules.Count > 0)
        {
            return doctor.Schedules.ToList();
        }

        return Enum.GetValues<DayOfWeek>()
            .Where(d => d is not DayOfWeek.Sunday)
            .Select(d => new DoctorSchedule
            {
                DoctorId = doctor.Id,
                DayOfWeek = d,
                StartTime = new TimeSpan(9, 0, 0),
                EndTime = new TimeSpan(18, 0, 0)
            })
            .ToList();
    }

    private async Task<AvailabilityResult> SoleDoctorOrNextAsync(
        Doctor doctor,
        string? timeHint,
        DateOnly date,
        int? durationMinutes,
        CancellationToken cancellationToken)
    {
        var allSlots = await LoadSlotsAsync(date, durationMinutes, cancellationToken);
        var sole = SoleDoctorAvailability(doctor, allSlots, timeHint, date);
        if (sole.Slots.Count > 0)
        {
            return sole;
        }

        for (var offset = 1; offset <= 7; offset++)
        {
            var nextDate = date.AddDays(offset);
            var nextSlots = await LoadSlotsAsync(nextDate, durationMinutes, cancellationToken);
            var next = SoleDoctorAvailability(doctor, nextSlots, timeHint, nextDate);
            if (next.Slots.Count == 0)
            {
                continue;
            }

            var window = ClinicDateParser.ParseTimeWindow(timeHint);
            var spoken = window.Restricts
                ? $"{doctor.Name} has no {window.Label} openings on {SpeakDate(date)}. Next {window.Label} is {SpeakDate(nextDate)} at {SpeakSlotTimes(next.Slots, 5)}. Ask which time they want. Stay on the line. Never hang up."
                : $"{doctor.Name} has no slots on {SpeakDate(date)}. {AskWindowsSpoken(doctor.Name, nextDate, next.Slots)}";
            return next with { Message = spoken, SpokenPrompt = spoken };
        }

        var none = $"{doctor.Name} has no openings in the next week. Offer a callback. Stay on the line. Never hang up.";
        return sole with { Message = none, SpokenPrompt = none };
    }

    private static AvailabilityResult AskWhichDoctor(
        IReadOnlyList<Doctor> doctors,
        DateOnly date,
        IReadOnlyList<AvailableSlotDto> allSlots,
        string? timeHint)
    {
        var window = ClinicDateParser.ParseTimeWindow(timeHint);
        var slots = allSlots.ToList();
        if (window.Restricts)
        {
            slots = slots
                .Where(s => ClinicDateParser.TryParseSlotLocal(s.LocalStart, out var slotTime) && window.Matches(slotTime.TimeOfDay))
                .ToList();
        }

        var summaries = doctors
            .Select(d => new AvailableDoctorDto(d.Id, d.Name, d.Specialization, allSlots.Count(s => s.DoctorId == d.Id)))
            .ToList();
        var names = string.Join(", ", doctors.Select(d => $"{d.Name} ({d.Specialization})"));
        string spoken;
        if (window.Restricts && slots.Count > 0)
        {
            var bits = doctors.Select(doctor =>
            {
                var times = SpeakSlotTimes(slots.Where(s => s.DoctorId == doctor.Id), 3);
                return slots.Any(s => s.DoctorId == doctor.Id)
                    ? $"{doctor.Name} {window.Label} {times}"
                    : $"{doctor.Name}, no {window.Label} slots";
            });
            spoken = $"Clinic doctors {SpeakDate(date)} {window.Label}: {string.Join(". ", bits)}. Ask which doctor and which time. Stay on the line. Never hang up.";
            return new AvailabilityResult(true, spoken, spoken, "", true, summaries, slots);
        }

        spoken = window.Restricts
            ? AskWindowsSpoken($"Clinic doctors {names}", date, allSlots)
                .Replace("Do not read specific times yet.", $"No {window.Label} openings. Ask another window. Never say no slots are available.")
            : $"{AskWindowsSpoken($"Clinic doctors {names}", date, allSlots)} Ask which doctor they want. Do not invent names.";
        return new AvailabilityResult(
            summaries.Any(d => d.SlotCount > 0),
            spoken,
            spoken,
            "",
            true,
            summaries,
            allSlots.ToList());
    }

    private static AvailabilityResult OfferAnyoneAvailability(
        IReadOnlyList<Doctor> doctors,
        IReadOnlyList<AvailableSlotDto> allSlots,
        string? preferredTime,
        DateOnly date)
    {
        var window = ClinicDateParser.ParseTimeWindow(preferredTime);
        var slots = allSlots.ToList();
        if (window.Restricts)
        {
            slots = slots
                .Where(s => ClinicDateParser.TryParseSlotLocal(s.LocalStart, out var slotTime) && window.Matches(slotTime.TimeOfDay))
                .ToList();
        }

        var summaries = SummarizeDoctors(slots);
        if (!window.Restricts)
        {
            var ask = AskWindowsSpoken("A clinic doctor", date, slots);
            return new AvailabilityResult(slots.Count > 0, ask, ask, "", summaries.Count > 1, summaries, slots);
        }

        var offer = slots.OrderBy(s => s.LocalStart).Take(8).ToList();
        var spoken = offer.Count == 0
            ? AskWindowsSpoken("A clinic doctor", date, allSlots).Replace("Do not read specific times yet.", $"No {window.Label} openings. Ask another window. Do not say no slots are available if morning, afternoon, or evening is open.")
            : $"These doctors have {window.Label} times {SpeakDate(date)}: {string.Join(". ", summaries.Select(d => $"{d.DoctorName} at {SpeakSlotTimes(offer.Where(s => s.DoctorId == d.DoctorId), 3)}"))}. Ask who and which time. Stay on the line. Never hang up.";
        return new AvailabilityResult(offer.Count > 0 || allSlots.Count > 0, spoken, spoken, "", summaries.Count > 1, summaries, offer.Count > 0 ? offer : allSlots.ToList());
    }

    private static AvailabilityResult SoleDoctorAvailability(Doctor doctor, IReadOnlyList<AvailableSlotDto> allSlots, string? preferredTime, DateOnly date)
    {
        var daySlots = allSlots.Where(s => s.DoctorId == doctor.Id).ToList();
        var window = ClinicDateParser.ParseTimeWindow(preferredTime);
        var morning = SlotsInRange(daySlots, TimeSpan.FromHours(9), TimeSpan.FromHours(12));
        var afternoon = SlotsInRange(daySlots, TimeSpan.FromHours(12), TimeSpan.FromHours(16));
        var evening = SlotsInRange(daySlots, TimeSpan.FromHours(16), null);
        var slots = daySlots;
        if (window.Restricts)
        {
            slots = daySlots.Where(slot =>
                ClinicDateParser.TryParseSlotLocal(slot.LocalStart, out var slotTime)
                && window.Matches(slotTime.TimeOfDay)).ToList();
        }

        var name = doctor.Name;
        var spoken = BuildSoleSpoken(name, date, window, slots, morning, afternoon, evening);
        var offer = window.Restricts && slots.Count > 0 ? slots.Take(8).ToList() : daySlots;
        return new AvailabilityResult(
            daySlots.Count > 0,
            spoken,
            spoken,
            doctor.Specialization,
            false,
            [new AvailableDoctorDto(doctor.Id, doctor.Name, doctor.Specialization, daySlots.Count)],
            offer);
    }

    private static string BuildSoleSpoken(
        string name,
        DateOnly date,
        ClinicDateParser.TimeWindow window,
        IReadOnlyList<AvailableSlotDto> slots,
        IReadOnlyList<AvailableSlotDto> morning,
        IReadOnlyList<AvailableSlotDto> afternoon,
        IReadOnlyList<AvailableSlotDto> evening)
    {
        var when = SpeakDate(date);
        if (window.Restricts)
        {
            if (slots.Count > 0)
            {
                return $"{name} can see you {when} {window.Label} at {SpeakSlotTimes(slots, 5)}. Ask which time they want, then book it. Stay on the line. Never hang up.";
            }

            return AskWindowsSpoken(name, date, morning.Concat(afternoon).Concat(evening).ToList())
                .Replace("Do not read specific times yet.", $"No {window.Label} openings. Ask another of the open windows. Never say no slots are available.");
        }

        return AskWindowsSpoken(name, date, morning.Concat(afternoon).Concat(evening).ToList());
    }

    private static string AskWindowsSpoken(string who, DateOnly date, IReadOnlyList<AvailableSlotDto> slots)
    {
        var morning = SlotsInRange(slots, TimeSpan.FromHours(9), TimeSpan.FromHours(12));
        var afternoon = SlotsInRange(slots, TimeSpan.FromHours(12), TimeSpan.FromHours(16));
        var evening = SlotsInRange(slots, TimeSpan.FromHours(16), null);
        var open = new List<string>();
        if (morning.Count > 0) open.Add("morning");
        if (afternoon.Count > 0) open.Add("afternoon");
        if (evening.Count > 0) open.Add("evening");
        if (open.Count == 0)
        {
            return $"{who} has no openings on {SpeakDate(date)}. Offer another day. Stay on the line. Never hang up.";
        }

        var list = open.Count == 1
            ? open[0]
            : open.Count == 2
                ? $"{open[0]} or {open[1]}"
                : "morning, afternoon, and evening";
        var verb = who.StartsWith("Clinic doctors", StringComparison.OrdinalIgnoreCase) ? "are" : "is";
        return $"{who} {verb} available {SpeakDate(date)}. {char.ToUpperInvariant(list[0]) + list[1..]} slots are available. Ask only: morning, afternoon, or evening? Do not read specific times yet. Never say no slots available when evening, afternoon, or morning is open. Stay on the line. Never hang up.";
    }

    private static string ResolvePartOfDayHint(string? preferredTime, string? problem)
    {
        var fromPreferred = ClinicDateParser.CombineTimeHints(preferredTime);
        if (ClinicDateParser.ParseTimeWindow(fromPreferred).Restricts)
        {
            return fromPreferred;
        }

        var fromProblem = ClinicDateParser.ParseTimeWindow(problem);
        return fromProblem.Label is "morning" or "afternoon" or "evening"
            ? fromProblem.Label
            : fromPreferred;
    }

    private static List<AvailableSlotDto> SlotsInRange(IEnumerable<AvailableSlotDto> slots, TimeSpan from, TimeSpan? to) =>
        slots.Where(slot =>
            ClinicDateParser.TryParseSlotLocal(slot.LocalStart, out var time)
            && time.TimeOfDay >= from
            && (to is not TimeSpan end || time.TimeOfDay < end)).ToList();

    private static string SpeakDate(DateOnly date)
    {
        var today = DateOnly.FromDateTime(IndiaTime.Now);
        if (date == today)
        {
            return "today";
        }

        if (date == today.AddDays(1))
        {
            return "tomorrow";
        }

        return date.ToString("dddd d MMMM", CultureInfo.GetCultureInfo("en-IN"));
    }

    private static string SpeakSlotTimes(IEnumerable<AvailableSlotDto> slots, int max = 3)
    {
        var times = slots
            .Take(max)
            .Select(slot =>
            {
                if (ClinicDateParser.TryParseSlotLocal(slot.LocalStart, out var time))
                {
                    return time.ToString("h:mm tt", CultureInfo.GetCultureInfo("en-IN"));
                }

                return string.IsNullOrWhiteSpace(slot.Display) ? slot.LocalStart : slot.Display;
            })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        return times.Count switch
        {
            0 => "no listed times",
            1 => times[0],
            2 => $"{times[0]} or {times[1]}",
            _ => $"{times[0]}, {times[1]}, or {times[2]}"
        };
    }

    private static AvailabilityResult DoctorChoiceResult(
        bool available,
        string specialty,
        string kind,
        IReadOnlyList<AvailableDoctorDto> doctors,
        string? missingDoctor)
    {
        var names = doctors.Select(d => d.DoctorName).ToList();
        var listed = names.Count == 0 ? "none" : string.Join(" and ", names);
        var spoken = names.Count == 0
            ? $"No {kind} are available that day. Offer another day."
            : names.Count == 1
                ? $"The only available {kind.TrimEnd('s')} is {names[0]}. Say only this name. Do not mention any other doctor."
                : $"{names.Count} {kind} are available: {listed}. Say only these names and ask who they want. Do not book yet. Do not invent names.";
        var message = string.IsNullOrWhiteSpace(missingDoctor)
            ? spoken
            : $"{missingDoctor} is not available. {spoken}";
        return new AvailabilityResult(available, message, spoken, specialty, names.Count > 1, doctors, []);
    }

    private static List<AvailableDoctorDto> SummarizeDoctors(IEnumerable<AvailableSlotDto> slots) =>
        slots
            .GroupBy(s => new { s.DoctorId, s.DoctorName, s.Specialization })
            .Select(g => new AvailableDoctorDto(g.Key.DoctorId, g.Key.DoctorName, g.Key.Specialization, g.Count()))
            .OrderBy(d => d.DoctorName)
            .ToList();

    private async Task<List<AvailableSlotDto>> LoadSpecialtySlotsAsync(
        DateOnly date,
        int? durationMinutes,
        string specialty,
        CancellationToken cancellationToken)
    {
        var slots = await LoadSlotsAsync(date, durationMinutes, cancellationToken);
        return string.IsNullOrWhiteSpace(specialty)
            ? slots
            : slots.Where(s => DoctorSpecialties.Matches(s.Specialization, specialty)).ToList();
    }

    private static bool IsAnyone(string? doctorName)
    {
        if (string.IsNullOrWhiteSpace(doctorName))
        {
            return false;
        }

        var value = doctorName.Trim();
        var exact = new[]
        {
            "any", "anyone", "anybody", "all", "either", "whoever", "whatever",
            "no preference", "any doctor", "any dentist", "anyone will work", "anybody will work"
        };
        if (exact.Any(p => value.Equals(p, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var lower = value.ToLowerInvariant();
        return lower.Contains("anyone")
            || lower.Contains("anybody")
            || lower.Contains("whoever")
            || lower.Contains("no preference")
            || lower.Contains("any doctor")
            || lower.Contains("any dentist")
            || lower.Contains("koi bhi")
            || lower.Contains("koi bhee");
    }

    private static string SpecialtyLabel(string specialty) =>
        string.IsNullOrWhiteSpace(specialty) ? "doctors"
        : specialty.Equals("Dentist", StringComparison.OrdinalIgnoreCase) ? "dentists"
        : specialty.ToLowerInvariant() + "s";

    private static string? NormalizeDoctorChoice(string? doctorName)
    {
        if (string.IsNullOrWhiteSpace(doctorName) || IsAnyone(doctorName))
        {
            return null;
        }

        return doctorName.Trim();
    }

    private static List<AvailableSlotDto> FilterToSpecialty(IEnumerable<AvailableSlotDto> slots, string? specialty) =>
        string.IsNullOrWhiteSpace(specialty)
            ? slots.ToList()
            : slots.Where(s => DoctorSpecialties.Matches(s.Specialization, specialty)).ToList();

    private static bool DoctorNameMatches(string doctorName, string requested)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            return false;
        }

        if (doctorName.Contains(requested.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var a = CollapseName(doctorName);
        var b = CollapseName(requested);
        if (a.Length == 0 || b.Length == 0)
        {
            return false;
        }

        if (a.Contains(b) || b.Contains(a))
        {
            return true;
        }

        return b.Contains("meheta") && a.Contains("mehta");
    }

    private static string CollapseName(string value)
    {
        var text = value.Trim().ToLowerInvariant();
        if (text.StartsWith("dr.", StringComparison.Ordinal))
        {
            text = text[3..];
        }
        else if (text.StartsWith("dr ", StringComparison.Ordinal))
        {
            text = text[3..];
        }

        return Regex.Replace(text, @"[^a-z]", "");
    }

    private static string MatchSpecialty(string? problem, IEnumerable<string>? clinicSpecs = null)
    {
        var text = (problem ?? string.Empty).ToLowerInvariant();
        var specs = clinicSpecs?.ToList() ?? [];
        bool Has(string needed) => specs.Any(s => DoctorSpecialties.Matches(s, needed));

        if (text.Contains("tooth") || text.Contains("teeth") || text.Contains("dental") || text.Contains("dentist")
            || text.Contains("cavity") || text.Contains("gum") || text.Contains("daant") || text.Contains("dant")
            || text.Contains("daanton") || text.Contains("toothache") || text.Contains("wisdom"))
        {
            return DoctorSpecialties.Dentist;
        }

        if (text.Contains("child") || text.Contains("kid") || text.Contains("pediatric") || text.Contains("baby"))
        {
            return DoctorSpecialties.Pediatrics;
        }

        if (text.Contains("skin") || text.Contains("rash") || text.Contains("acne") || text.Contains("derma"))
        {
            return DoctorSpecialties.Dermatology;
        }

        if (text.Contains("fracture") || text.Contains("bone") || text.Contains("haddi") || text.Contains("ortho")
            || text.Contains("foot") || text.Contains("pair") || text.Contains("paer") || text.Contains("leg")
            || text.Contains("taang") || text.Contains("sprain") || text.Contains("injury") || text.Contains("toota")
            || text.Contains("chot"))
        {
            return Has(DoctorSpecialties.Orthopedics)
                ? DoctorSpecialties.Orthopedics
                : DoctorSpecialties.GeneralPhysician;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        if (text.Contains("fever") || text.Contains("cough") || text.Contains("cold") || text.Contains("bukhar")
            || text.Contains("general") || text.Contains("pain") || text.Contains("dard"))
        {
            return DoctorSpecialties.GeneralPhysician;
        }

        return DoctorSpecialties.GeneralPhysician;
    }

    private static string BookingSuccessMessage(string patientName, string doctorName, DateTime when) =>
        $"Okay, booking will be created for {patientName}. Your booking has been successfully created. {when:h:mm tt} IST on {when:dddd d MMMM} with {doctorName}.";

    private async Task<string> ResolveSpokenNameAsync(
        string? callerName,
        string? callerPhone,
        string? spokenText,
        CallLog? existing,
        CancellationToken cancellationToken)
    {
        var onPhone = await PatientsOnPhoneAsync(callerPhone, cancellationToken);
        foreach (var known in onPhone)
        {
            await _ai.EnsureEnglishAsync(known, cancellationToken);
        }

        var mentioned = onPhone
            .Where(p => CallerIdentity.HasRealName(p.Name) && CallerIdentity.TextMentionsName(spokenText, p.Name))
            .Select(p => p.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (mentioned.Count == 1)
        {
            return mentioned[0];
        }

        if (CallerIdentity.HasRealName(callerName))
        {
            var matched = onPhone.FirstOrDefault(p => CallerIdentity.NamesMatch(p.Name, callerName));
            return matched?.Name.Trim() ?? callerName!.Trim();
        }

        var guessed = CallLogDetails.ExtractCallerName(spokenText ?? "");
        if (CallerIdentity.HasRealName(guessed))
        {
            var matched = onPhone.FirstOrDefault(p => CallerIdentity.NamesMatch(p.Name, guessed));
            return matched?.Name.Trim() ?? guessed.Trim();
        }

        var cached = _inbound.RecentNameFor(callerPhone);
        if (CallerIdentity.HasRealName(cached))
        {
            return cached!.Trim();
        }

        if (existing is not null && CallerIdentity.HasRealName(existing.CallerName)
            && CallerIdentity.PhonesMatch(existing.CallerPhone, callerPhone))
        {
            return existing.CallerName.Trim();
        }

        return "Unknown";
    }

    private async Task<(Patient? Patient, string Ask)> ResolveBookingPatientAsync(
        string callerName,
        string callerPhone,
        CancellationToken cancellationToken)
    {
        var ask = "Can you please confirm your full name and number?";
        if (!CallerIdentity.HasRealName(callerName))
        {
            return (null, ask);
        }

        var last10 = CallerIdentity.Last10(callerPhone);
        var patients = await _db.Patients.ToListAsync(cancellationToken);
        foreach (var row in patients)
        {
            await _ai.EnsureEnglishAsync(row, cancellationToken);
        }

        var named = patients
            .Where(p => CallerIdentity.NamesMatch(p.Name, callerName))
            .OrderBy(p => p.Id)
            .ToList();
        var matched = named.FirstOrDefault(p => !CallerIdentity.IsMissingPhone(p.Contact) && CallerIdentity.Last10(p.Contact) == last10);
        if (matched is not null)
        {
            return (matched, "");
        }

        if (named.Count > 0)
        {
            return (null, $"I found {named[0].Name} in our records, but this phone number does not match. {ask}");
        }

        var parts = CallerIdentity.FoldName(callerName).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
        {
            return (null, ask);
        }

        return (await FindOrCreatePatientAsync(callerName, callerPhone, cancellationToken), "");
    }

    private async Task<Patient> FindOrCreatePatientAsync(string callerName, string callerPhone, CancellationToken cancellationToken)
    {
        var phone = CallerIdentity.ResolvePhone(callerPhone);
        callerName = await _ai.TranslateToEnglishAsync(callerName, cancellationToken);
        var onPhone = await PatientsOnPhoneAsync(phone, cancellationToken);
        foreach (var known in onPhone)
        {
            await _ai.EnsureEnglishAsync(known, cancellationToken);
            if (CallerIdentity.IsMissingPhone(known.Contact))
            {
                known.Contact = phone;
            }
        }
        if (!CallerIdentity.HasRealName(callerName))
        {
            callerName = "Unknown Caller";
        }

        var name = callerName.Trim();
        var existing = onPhone.FirstOrDefault(p => CallerIdentity.NamesMatch(p.Name, name));
        if (existing is not null)
        {
            existing.Contact = phone;
            if (CallerIdentity.HasRealName(name)
                && (existing.Name.StartsWith("Unknown", StringComparison.OrdinalIgnoreCase)
                    || (name.Length > existing.Name.Length && CallerIdentity.NamesMatch(existing.Name, name))))
            {
                existing.Name = name;
            }

            var stamp = $"Voice update {IndiaTime.Now:yyyy-MM-dd HH:mm} IST";
            if (string.IsNullOrWhiteSpace(existing.Notes))
            {
                existing.Notes = stamp;
            }
            else if (!existing.Notes.Contains(stamp, StringComparison.Ordinal))
            {
                existing.Notes = $"{existing.Notes.Trim()}; {stamp}";
            }

            await _db.SaveChangesAsync(cancellationToken);
            return existing;
        }

        var patient = new Patient
        {
            Name = name,
            Contact = phone,
            Notes = onPhone.Count > 0
                ? $"Created from Sarvam Voicebot on shared number {CallerIdentity.DisplayPhone(phone)}"
                : "Created from Sarvam Voicebot call"
        };
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync(cancellationToken);
        patient.Uhid = $"ANJ-{patient.Id:D6}";
        await _db.SaveChangesAsync(cancellationToken);
        return patient;
    }

    private async Task<List<Patient>> PatientsOnPhoneAsync(string? phone, CancellationToken cancellationToken)
    {
        var last10 = CallerIdentity.Last10(phone);
        if (last10.Length < 10)
        {
            return [];
        }

        var patients = await _db.Patients.ToListAsync(cancellationToken);
        return patients.Where(p => CallerIdentity.Last10(p.Contact) == last10).OrderBy(p => p.Id).ToList();
    }

    private static DateTime ResolveCallTimestamp(DateTime? startedAt)
    {
        if (startedAt is null || startedAt.Value == default)
        {
            return DateTime.UtcNow;
        }

        var value = startedAt.Value;
        if (value.Kind == DateTimeKind.Utc)
        {
            return value;
        }

        if (value.Kind == DateTimeKind.Local)
        {
            return value.ToUniversalTime();
        }

        return IndiaTime.ToUtcFromIst(value);
    }

    private static string FormatLocal(DateTime value) =>
        value.ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    private static string NormalizePhone(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var digits = Regex.Replace(value, @"\D", "");
        if (digits.Length == 10)
        {
            return "+91" + digits;
        }

        if (digits.Length == 11 && digits.StartsWith('0'))
        {
            return "+91" + digits[1..];
        }

        if (digits.Length == 12 && digits.StartsWith("91"))
        {
            return "+" + digits;
        }

        return value.Trim();
    }

    private static string Last10(string? value)
    {
        var digits = Regex.Replace(value ?? "", @"\D", "");
        return digits.Length >= 10 ? digits[^10..] : digits;
    }

    private static string MapRole(string role) =>
        role.Contains("agent", StringComparison.OrdinalIgnoreCase) || role.Contains("assistant", StringComparison.OrdinalIgnoreCase)
            ? "agent"
            : "caller";

    private static void MarkCallbackRequested(CallLog callLog, string? note)
    {
        callLog.Intent = "Callback";
        callLog.Outcome = "Callback";
        callLog.TransferType = "Callback";
        callLog.ActionTaken = "Callback requested";
        if (CallLogDetails.IsGenericSummary(callLog.Summary))
        {
            callLog.Summary = string.IsNullOrWhiteSpace(note) || CallLogDetails.IsGenericSummary(note)
                ? "Caller requested a callback."
                : note.Trim();
        }
    }

    private async Task<CallCallback> EnsureImportedCallbackAsync(
        CallLog callLog,
        string reason,
        CancellationToken cancellationToken)
    {
        var queued = await _db.CallCallbacks
            .Where(c => c.Status == "Queued")
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        var existing = CallLogDetails.FindCallback(callLog, queued);
        if (existing is not null)
        {
            return existing;
        }

        return await QueueImportedCallbackAsync(callLog, reason, cancellationToken);
    }

    private async Task<CallCallback> QueueImportedCallbackAsync(
        CallLog callLog,
        string reason,
        CancellationToken cancellationToken)
    {
        var callback = new CallCallback
        {
            CallerName = CallLogDetails.DisplayName(callLog.CallerName),
            CallerPhone = CallLogDetails.DisplayPhone(callLog.CallerPhone),
            Reason = reason,
            Summary = CallLogDetails.IsGenericSummary(callLog.Summary) ? "Caller requested a callback." : callLog.Summary,
            Status = "Queued",
            Priority = false,
            CreatedAt = DateTime.UtcNow
        };
        _db.CallCallbacks.Add(callback);
        await _db.SaveChangesAsync(cancellationToken);
        await _hub.Clients.All.SendAsync(
            "CallbackQueued",
            new { callerName = callback.CallerName, callerPhone = callback.CallerPhone, summary = callback.Summary },
            cancellationToken);
        return callback;
    }

    private static string GuessIntent(string text)
    {
        var value = text.ToLowerInvariant();
        if (CallLogDetails.LooksForwarded(value))
        {
            return "Human";
        }

        if (CallLogDetails.WantsCallback(value))
        {
            return "Callback";
        }

        if (value.Contains("appoint") || value.Contains("book") || value.Contains("slot"))
        {
            return "Appointment";
        }

        if (value.Contains("cancel") || value.Contains("reschedul"))
        {
            return "Reschedule";
        }

        if (value.Contains("bill") || value.Contains("payment"))
        {
            return "Billing";
        }

        if (value.Contains("hour") || value.Contains("open"))
        {
            return "Hours";
        }

        return "Query";
    }

    private static string Truncate(string value, int max) =>
        value.Length <= max ? value : value[..max];
}
