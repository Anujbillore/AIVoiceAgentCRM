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
    IReadOnlyList<AvailableSlotDto> Alternatives);

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

public interface ISarvamManagedService
{
    Task<ClinicRosterDto> GetClinicRosterAsync(CancellationToken cancellationToken = default);
    Task<AvailabilityResult> GetAvailabilityAsync(DateOnly date, int? durationMinutes, string? problem, string? doctorName, string? preferredTime, CancellationToken cancellationToken = default);
    Task<SarvamBookResult> BookAsync(DateTime localStart, string callerName, string callerPhone, string purpose, string? doctorName, CancellationToken cancellationToken = default);
    Task<SarvamCallImportResult> ImportCallAsync(SarvamCallImportRequest request, CancellationToken cancellationToken = default);
}

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
        if (bookable.Count == 1)
        {
            return SoleDoctorAvailability(bookable[0], allSlots, preferredTime);
        }

        var clinicSpecs = bookable.Select(d => d.Specialization).ToList();
        var specialty = MatchSpecialty(problem, clinicSpecs);
        var namedDoctor = NormalizeDoctorChoice(doctorName);
        var kind = SpecialtyLabel(specialty);

        if (TryParsePreferredTime(preferredTime, out var timeOfDay))
        {
            allSlots = allSlots
                .Where(s => DateTime.TryParse(s.LocalStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var slotTime)
                    && Math.Abs((slotTime.TimeOfDay - timeOfDay).TotalMinutes) <= 30)
                .ToList();
        }

        if (!string.IsNullOrWhiteSpace(namedDoctor))
        {
            var chosen = allSlots
                .Where(s => DoctorNameMatches(s.DoctorName, namedDoctor))
                .Take(8)
                .ToList();
            if (chosen.Count > 0)
            {
                return new AvailabilityResult(
                    true,
                    $"{chosen[0].DoctorName} ({chosen[0].Specialization}) has {chosen.Count} slots. Offer only these times.",
                    $"Offer only these times with {chosen[0].DoctorName}.",
                    chosen[0].Specialization,
                    false,
                    SummarizeDoctors(chosen),
                    chosen);
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
            return DoctorChoiceResult(true, specialty, kind, doctors, null);
        }

        return new AvailabilityResult(
            slots.Count > 0,
            slots.Count > 0
                ? $"{(doctors.Count == 1 ? doctors[0].DoctorName + " is available." : "Slots are available.")} Offer the returned times."
                : $"No {kind} are available on that date. Offer another day. Do not end the call.",
            slots.Count > 0
                ? (doctors.Count == 1
                    ? $"The only available {kind.TrimEnd('s')} is {doctors[0].DoctorName}. Say only this name, then offer the times."
                    : "Offer the returned times.")
                : $"No {kind} are available that day. Offer another day.",
            specialty,
            false,
            doctors,
            slots.Take(8).ToList());
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
        if (string.IsNullOrWhiteSpace(callerPhone))
        {
            callerPhone = _inbound.RecentPhone() ?? "";
        }

        if (string.IsNullOrWhiteSpace(callerName) || callerName.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            callerName = _inbound.RecentName() ?? callerName;
        }

        _inbound.Remember(callerPhone, callerName);
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

        var patient = await FindOrCreatePatientAsync(callerName, callerPhone, cancellationToken);
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
                $"Already booked {already.ScheduledAt:h:mm tt} IST with {already.Doctor.Name} for {patient.Name}. Say this doctor's name. Do not book again.",
                []);
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
                $"Booked {booked.ScheduledAt:h:mm tt} IST with {booked.DoctorName} for {booked.PatientName}. Say the doctor name {booked.DoctorName}.",
                []);
        }
        catch (InvalidOperationException ex)
        {
            var alternatives = await LoadSlotsAsync(DateOnly.FromDateTime(localStart), 30, cancellationToken);
            return new SarvamBookResult(false, null, FormatLocal(localStart), null, ex.Message + " Do not end the call.", alternatives.Take(3).ToList());
        }
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
        var callerPhone = CallLogDetails.DisplayPhone(NormalizePhone(request.CallerPhone));
        var callerName = string.IsNullOrWhiteSpace(request.CallerName) || request.CallerName.Contains("identifier", StringComparison.OrdinalIgnoreCase)
            ? "Unknown"
            : request.CallerName.Trim();
        var transcript = string.Join(" | ", request.Transcript
            .Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => $"{MapRole(t.Role)}: {t.Text.Trim()}"));
        if (string.IsNullOrWhiteSpace(callerPhone))
        {
            var cached = CallLogDetails.DisplayPhone(NormalizePhone(_inbound.RecentPhone()));
            callerPhone = string.IsNullOrWhiteSpace(cached) ? CallLogDetails.ExtractPhone(transcript) : cached;
        }

        _inbound.Remember(callerPhone, callerName);

        if (callerName == "Unknown")
        {
            var guessed = CallLogDetails.ExtractCallerName(transcript);
            if (!string.IsNullOrWhiteSpace(guessed))
            {
                callerName = guessed;
            }
        }

        var summary = string.IsNullOrWhiteSpace(request.Summary)
            ? (string.IsNullOrWhiteSpace(transcript) ? "Sarvam Voicebot inbound call" : Truncate(transcript, 280))
            : Truncate(request.Summary.Trim(), 2000);
        var existing = await FindExistingCallAsync(externalId, callerPhone, cancellationToken);
        if (existing is not null)
        {
            MergeCall(existing, callerName, callerPhone, summary, transcript, request.Status, request.FailureReason);
            var existingRelated = await FindRelatedAppointmentAsync(existing, cancellationToken);
            if (existingRelated is not null)
            {
                ApplyBookingToCall(existing, existingRelated);
            }

            await _ai.EnsureEnglishAsync(existing, cancellationToken);
            await _db.SaveChangesAsync(cancellationToken);
            await _hub.Clients.All.SendAsync("CallSummaryAdded", CallLogDetails.ToDto(existing, existingRelated), cancellationToken);
            return new SarvamCallImportResult(existing.Id, true, true, null);
        }
        var intent = string.IsNullOrWhiteSpace(request.Intent)
            ? GuessIntent($"{request.Status} {request.FailureReason} {summary} {transcript}")
            : request.Intent.Trim();
        var classifyText = $"{request.Status} {request.FailureReason} {summary} {transcript} {intent}";
        var patient = string.IsNullOrWhiteSpace(callerPhone) && callerName == "Unknown"
            ? null
            : await FindOrCreatePatientAsync(callerName, callerPhone, cancellationToken);

        var timestamp = request.StartedAt?.ToUniversalTime() ?? DateTime.UtcNow;
        var duration = request.DurationSeconds is > 0
            ? $" ({Math.Round(request.DurationSeconds.Value)}s)"
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
            PatientId = patient?.Id
        };

        _db.CallLogs.Add(callLog);
        await _db.SaveChangesAsync(cancellationToken);

        var related = await FindRelatedAppointmentAsync(callLog, cancellationToken);
        CallCallback? callback = null;
        if (related is not null)
        {
            ApplyBookingToCall(callLog, related);
            await _db.SaveChangesAsync(cancellationToken);
        }
        else if (forwarded)
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
        else if (CallLogDetails.IsCallbackRequested(callLog, null, null))
        {
            callLog.Intent = "Callback";
            callLog.Outcome = "Callback";
            callLog.ActionTaken = "Callback requested";
            callback = await QueueImportedCallbackAsync(
                callLog,
                "AI did not resolve the call; caller asked for a callback",
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
        var existing = await FindExistingCallAsync($"sarvam-book:{booked.Id}", phone, cancellationToken);
        var callLog = existing ?? new CallLog
        {
            ExternalId = $"sarvam-book:{booked.Id}",
            Timestamp = DateTime.UtcNow,
            Intent = "Appointment",
            Outcome = "Contained"
        };
        callLog.CallerName = patient.Name;
        callLog.CallerPhone = phone;
        callLog.PatientId = patient.Id;
        callLog.Summary = $"{patient.Name} booked with {booked.DoctorName} for {booked.ScheduledAt:ddd d MMM, h:mm tt} IST.";
        callLog.ActionTaken = $"Booked with {booked.DoctorName} at {booked.ScheduledAt:h:mm tt} IST";
        callLog.Transcript = string.IsNullOrWhiteSpace(callLog.Transcript) ? callLog.Summary : callLog.Transcript;
        if (existing is null)
        {
            _db.CallLogs.Add(callLog);
        }

        await _ai.EnsureEnglishAsync(callLog, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
        var appointment = await _db.Appointments.Include(a => a.Doctor).Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.Id == booked.Id, cancellationToken);
        await _hub.Clients.All.SendAsync("CallSummaryAdded", CallLogDetails.ToDto(callLog, appointment), cancellationToken);
    }

    private async Task<CallLog?> FindExistingCallAsync(string externalId, string phone, CancellationToken cancellationToken)
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

        var since = DateTime.UtcNow.AddHours(-6);
        var matches = await _db.CallLogs
            .Where(c => c.Timestamp >= since)
            .OrderByDescending(c => c.Timestamp)
            .ToListAsync(cancellationToken);
        return matches.FirstOrDefault(c => CallLogDetails.Last10(c.CallerPhone) == last10);
    }

    private static void MergeCall(CallLog existing, string callerName, string callerPhone, string summary, string transcript, string status, string? failureReason)
    {
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

        if (existing.ActionTaken.StartsWith("Sarvam Voicebot", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(status))
        {
            existing.ActionTaken = $"Sarvam Voicebot {status}";
        }
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
            foreach (var schedule in doctor.Schedules.Where(s => s.DayOfWeek == date.DayOfWeek))
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
        var active = await _db.Doctors
            .Include(d => d.Schedules)
            .Where(d => d.IsActive)
            .OrderBy(d => d.Id)
            .ToListAsync(cancellationToken);
        var gp = active
            .Where(d => DoctorSpecialties.Matches(d.Specialization, DoctorSpecialties.GeneralPhysician))
            .ToList();
        var chosen = gp.Count > 0 ? gp.Take(1).ToList() : active.Take(1).ToList();
        if (chosen.Count == 1 && chosen[0].Schedules.Count == 0)
        {
            chosen[0].Schedules = Enum.GetValues<DayOfWeek>()
                .Where(d => d is not DayOfWeek.Sunday)
                .Select(d => new DoctorSchedule
                {
                    DoctorId = chosen[0].Id,
                    DayOfWeek = d,
                    StartTime = new TimeSpan(9, 0, 0),
                    EndTime = new TimeSpan(18, 0, 0)
                })
                .ToList();
            await _db.SaveChangesAsync(cancellationToken);
        }

        return chosen;
    }

    private static AvailabilityResult SoleDoctorAvailability(Doctor doctor, IReadOnlyList<AvailableSlotDto> allSlots, string? preferredTime)
    {
        var slots = allSlots.Where(s => s.DoctorId == doctor.Id).ToList();
        if (TryParsePreferredTime(preferredTime, out var timeOfDay))
        {
            slots = slots
                .Where(s => DateTime.TryParse(s.LocalStart, CultureInfo.InvariantCulture, DateTimeStyles.None, out var slotTime)
                    && Math.Abs((slotTime.TimeOfDay - timeOfDay).TotalMinutes) <= 30)
                .ToList();
        }

        var name = doctor.Name;
        var spoken = slots.Count > 0
            ? $"{name} is the only doctor, General Physician. Do not ask which doctor. Offer these times and book with {name}."
            : $"{name} has no free slots that day. Offer another day. Do not mention any other doctor.";
        return new AvailabilityResult(
            slots.Count > 0,
            spoken,
            spoken,
            doctor.Specialization,
            false,
            [new AvailableDoctorDto(doctor.Id, doctor.Name, doctor.Specialization, slots.Count)],
            slots.Take(8).ToList());
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

    private static bool TryParsePreferredTime(string? value, out TimeSpan timeOfDay)
    {
        timeOfDay = default;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var text = value.Trim();
        string[] formats = ["h:mm tt", "h tt", "htt", "HH:mm", "H:mm", "h:mmtt", "hh:mm tt"];
        if (DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out var parsed)
            || DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsed))
        {
            timeOfDay = parsed.TimeOfDay;
            return true;
        }

        return false;
    }

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

    private async Task<Patient> FindOrCreatePatientAsync(string callerName, string callerPhone, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(callerName) ? "Unknown Caller" : callerName.Trim();
        var phone = NormalizePhone(callerPhone);
        var last10 = Last10(phone);

        var patients = await _db.Patients.ToListAsync(cancellationToken);
        var existing = patients.FirstOrDefault(p =>
            (!string.IsNullOrEmpty(last10) && Last10(p.Contact) == last10)
            || (!string.IsNullOrWhiteSpace(name) && name != "Unknown Caller" && p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
        if (existing is not null)
        {
            if (string.IsNullOrWhiteSpace(existing.Contact) && !string.IsNullOrWhiteSpace(phone))
            {
                existing.Contact = phone;
                await _db.SaveChangesAsync(cancellationToken);
            }

            return existing;
        }

        var patient = new Patient
        {
            Name = name,
            Contact = phone,
            Notes = "Created from Sarvam Voicebot call"
        };
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync(cancellationToken);
        patient.Uhid = $"ANJ-{patient.Id:D6}";
        await _db.SaveChangesAsync(cancellationToken);
        return patient;
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
            Summary = callLog.Summary,
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
