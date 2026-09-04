using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Hubs;
using AiVoicePortal.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Services;

public interface IVoiceAgentService
{
    Task<VoiceStatusDto> GetStatusAsync(string publicBaseUrl, CancellationToken cancellationToken = default);
    Task<VoiceSessionDto> StartAsync(VoiceSessionStartRequest request, string? externalCallId = null, CancellationToken cancellationToken = default);
    Task<VoiceSessionDto> TurnAsync(string sessionId, string spokenText, CancellationToken cancellationToken = default);
    Task<VoiceSessionDto> EndAsync(string sessionId, CancellationToken cancellationToken = default);
    Task<VoiceTurnResponse> HandleIncomingAsync(SimulateCallRequest request, CancellationToken cancellationToken = default);
}

public class VoiceAgentService : IVoiceAgentService
{
    private readonly AppDbContext _db;
    private readonly ISarvamAiService _ai;
    private readonly IAppointmentService _appointments;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly IVoiceSessionStore _sessions;
    private readonly IExotelMediaService _exotel;
    private readonly IFinanceService _finance;
    private readonly IEmailService _email;

    public VoiceAgentService(
        AppDbContext db,
        ISarvamAiService ai,
        IAppointmentService appointments,
        IHubContext<DashboardHub> hub,
        IVoiceSessionStore sessions,
        IExotelMediaService exotel,
        IFinanceService finance,
        IEmailService email)
    {
        _db = db;
        _ai = ai;
        _appointments = appointments;
        _hub = hub;
        _sessions = sessions;
        _exotel = exotel;
        _finance = finance;
        _email = email;
    }

    public async Task<VoiceStatusDto> GetStatusAsync(string publicBaseUrl, CancellationToken cancellationToken = default)
    {
        var settings = await _db.AiSettings.FirstAsync(cancellationToken);
        var root = publicBaseUrl.TrimEnd('/');
        return new VoiceStatusDto(
            _ai.IsConfigured,
            settings.AgentName,
            settings.WelcomeMessage,
            $"{root}/api/voice/twilio/incoming",
            $"{root}/api/voice/twilio/gather",
            settings.Language.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
            $"{root}/api/voice/exotel/incoming",
            $"{root}/api/voice/exotel/turn",
            $"{root}/api/voice/exotel/audio",
            _exotel.IsConfigured,
            root,
            _ai.IsConfigured);
    }

    public async Task<VoiceSessionDto> StartAsync(VoiceSessionStartRequest request, string? externalCallId = null, CancellationToken cancellationToken = default)
    {
        var settings = await _db.AiSettings.FirstAsync(cancellationToken);
        var session = _sessions.Create(request.CallerName, request.CallerPhone, externalCallId);
        await IdentifyCallerAsync(session, cancellationToken);
        session.AfterHours = !await IsClinicOpenAsync(cancellationToken);
        session.Phase = "Consent";

        var intro = string.IsNullOrWhiteSpace(settings.WelcomeMessage)
            ? $"Hello, this is {settings.AgentName}. How can I help you today?"
            : settings.WelcomeMessage;
        var consent = string.IsNullOrWhiteSpace(settings.ConsentMessage)
            ? "This call may be recorded and handled by our clinic AI. You can ask for a person at any time."
            : settings.ConsentMessage;
        var hours = session.AfterHours
            ? " The clinic is currently closed. I can still help with an emergency or schedule a callback."
            : "";
        var vip = session.IsVip ? " I see you are a priority patient. I can connect you to the desk whenever you prefer." : "";
        var opening = $"{intro} {consent}{hours}{vip}".Trim();

        session.Turns.Add(new VoiceChatTurn("agent", opening, "Greeting"));
        return await ToSessionDtoAsync(session, settings, opening, "Greeting", "Inbound call routed", "Greeting and consent played", null, null, cancellationToken);
    }

    public async Task<VoiceSessionDto> TurnAsync(string sessionId, string spokenText, CancellationToken cancellationToken = default)
    {
        var session = _sessions.Get(sessionId) ?? throw new InvalidOperationException("Call session was not found.");
        if (session.Ended)
        {
            var settingsEnded = await _db.AiSettings.FirstAsync(cancellationToken);
            return await ToSessionDtoAsync(session, settingsEnded, "This call has already ended.", "Ended", "Call already closed", "No action", null, null, cancellationToken);
        }

        var text = spokenText.Trim();
        session.Turns.Add(new VoiceChatTurn("caller", text, ""));
        return await ProcessTurnAsync(session, text, cancellationToken);
    }

    public async Task<VoiceSessionDto> EndAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        var session = _sessions.Get(sessionId) ?? throw new InvalidOperationException("Call session was not found.");
        session.Ended = true;
        var settings = await _db.AiSettings.FirstAsync(cancellationToken);
        var goodbye = "Thank you for calling Anuj Clinic. Goodbye.";
        if (!session.Turns.Any(t => t.Role == "agent" && t.Text == goodbye))
        {
            session.Turns.Add(new VoiceChatTurn("agent", goodbye, "Ended"));
        }

        var log = await EnsureLogAsync(session, "Call ended", "Hung up", "Ended", cancellationToken);
        return await ToSessionDtoAsync(session, settings, goodbye, "Ended", "Caller hung up", "Call closed", log, null, cancellationToken);
    }

    public async Task<VoiceTurnResponse> HandleIncomingAsync(SimulateCallRequest request, CancellationToken cancellationToken = default)
    {
        var started = await StartAsync(new VoiceSessionStartRequest(request.CallerName, request.CallerPhone), null, cancellationToken);
        var spoken = request.SpokenText?.Trim() ?? string.Empty;
        var turn = string.IsNullOrWhiteSpace(spoken)
            ? started
            : await TurnAsync(started.SessionId, spoken, cancellationToken);
        if (!turn.Ended)
        {
            turn = await EndAsync(started.SessionId, cancellationToken);
        }

        return new VoiceTurnResponse(
            turn.AgentName,
            turn.WelcomeMessage,
            turn.ReplyText,
            turn.Intent,
            turn.Summary,
            turn.ActionTaken,
            turn.CallLog ?? new CallLogDto(0, request.CallerName, request.CallerPhone, turn.Summary, turn.ActionTaken, turn.Intent, spoken, DateTime.UtcNow),
            turn.Appointment,
            turn.AudioBase64);
    }

    private async Task<VoiceSessionDto> ProcessTurnAsync(VoiceSession session, string transcript, CancellationToken cancellationToken)
    {
        var settings = await _db.AiSettings.FirstAsync(cancellationToken);
        FillSlots(session, transcript);
        session.Sentiment = GuessSentiment(transcript);
        await IdentifyCallerAsync(session, cancellationToken);

        if (LooksLikeEmergency(transcript))
        {
            return await EscalateAsync(session, settings, "Emergency or sensitive topic", true, "Please stay on the line. I am connecting you to a clinician now.", "Emergency", cancellationToken);
        }

        if (LooksLikeHumanRequest(transcript) || LooksLikeBillingDispute(transcript))
        {
            var reason = LooksLikeBillingDispute(transcript) ? "Billing or insurance dispute" : "Caller asked for a person";
            return await EscalateAsync(session, settings, reason, session.IsVip || LooksLikeBillingDispute(transcript), "I will connect you with a team member. Please hold.", "Human", cancellationToken);
        }

        if (!session.ConsentGiven)
        {
            if (LooksLikeConsentRefusal(transcript))
            {
                return await EscalateAsync(session, settings, "Declined AI handling", false, "Understood. I will transfer you to the front desk.", "Human", cancellationToken);
            }

            session.ConsentGiven = true;
            session.Phase = "Collecting";
        }

        var intent = await _ai.DetectIntentAsync(transcript, settings, cancellationToken);
        if (LooksLikeEmergency(transcript))
        {
            intent = intent with { Intent = "Emergency", Confidence = 0.95 };
        }

        session.LastConfidence = intent.Confidence;
        var doctor = session.DoctorId.HasValue
            ? await _db.Doctors.Include(d => d.Schedules).FirstOrDefaultAsync(d => d.Id == session.DoctorId, cancellationToken)
            : await FindDoctorFromTextAsync(transcript, cancellationToken);
        if (doctor is not null)
        {
            session.DoctorId = doctor.Id;
        }

        session.PreferredTime ??= GuessTimeFromText(transcript);

        AppointmentDto? booked = null;
        var action = intent.ActionHint;
        var reply = intent.Reply;
        var ended = false;
        var label = intent.Intent;

        if (LooksLikeGoodbye(transcript) && intent.Intent is not ("Appointment" or "Reschedule" or "Cancel"))
        {
            label = "Ended";
            action = "Caller said goodbye";
            reply = "Thank you for calling Anuj Clinic. Goodbye.";
            ended = true;
            session.Outcome = string.IsNullOrWhiteSpace(session.Outcome) ? "Contained" : session.Outcome;
            session.Phase = "Ended";
        }
        else if (intent.Intent == "Spam" || LooksLikeSpam(transcript))
        {
            label = "Spam";
            action = "Ended politely";
            reply = "I am sorry, this line is only for clinic appointments. Goodbye.";
            ended = true;
            session.Outcome = "Contained";
            session.Phase = "Ended";
        }
        else if (intent.Intent == "Emergency")
        {
            return await EscalateAsync(session, settings, "Emergency or sensitive topic", true, "Please stay on the line. I am connecting you to a clinician now.", "Emergency", cancellationToken);
        }
        else if (intent.Intent == "Human")
        {
            return await EscalateAsync(session, settings, "Caller asked for a person", session.IsVip, "I will connect you with a team member. Please hold.", "Human", cancellationToken);
        }
        else if (intent.Intent == "Callback" || (session.AfterHours && LooksLikeCallback(transcript)))
        {
            return await QueueCallbackAsync(session, settings, intent.Summary, LooksLikeEmergency(transcript), cancellationToken);
        }
        else if (intent.Confidence < 0.65 && !LooksLikeBooking(transcript) && intent.Intent == "Query")
        {
            session.ClarifyingQuestions++;
            if (session.ClarifyingQuestions > 3 || (session.ClarifyingQuestions > 1 && intent.Confidence < 0.6))
            {
                return await EscalateAsync(session, settings, "Low confidence after clarification", false, "I want to make sure we help you correctly. I am transferring you to a team member.", "Query", cancellationToken);
            }

            label = "Clarify";
            action = "Asked a clarifying question";
            reply = session.IsVip
                ? "I want to be sure I have that right. Are you calling to book, check a bill, or speak with the desk?"
                : "Sorry, I missed that. Are you calling to book an appointment, ask clinic hours, or check a bill?";
            session.Phase = "Collecting";
        }
        else if (intent.Intent == "Billing" || LooksLikeBilling(transcript))
        {
            label = "Billing";
            (reply, action, ended) = await HandleBillingAsync(session, transcript, cancellationToken);
        }
        else if (intent.Intent == "Reschedule" || LooksLikeReschedule(transcript))
        {
            label = "Reschedule";
            (reply, action, ended, booked) = await HandleRescheduleAsync(session, doctor, cancellationToken);
        }
        else if (intent.Intent == "Cancel" || LooksLikeCancel(transcript))
        {
            label = "Cancel";
            (reply, action, ended) = await HandleCancelAsync(session, cancellationToken);
        }
        else if (intent.Intent == "Appointment" || session.DoctorId.HasValue || LooksLikeBooking(transcript))
        {
            label = "Appointment";
            session.Phase = "Collecting";
            var doctors = await ActiveDoctorsAsync(cancellationToken);
            if (session.DoctorId is null)
            {
                session.ClarifyingQuestions++;
                if (session.ClarifyingQuestions > 3)
                {
                    return await EscalateAsync(session, settings, "More than 3 clarifying questions", false, "I will have the front desk finish this booking for you. Please hold.", "Appointment", cancellationToken);
                }

                action = "Asked for preferred doctor";
                reply = doctors.Count == 0
                    ? "I do not have an active doctor on the roster right now. I can schedule a callback."
                    : $"I can help you book. Who would you like to see? We have {string.Join(", ", doctors.Select(d => d.Name))}.";
            }
            else if (session.PreferredTime is null)
            {
                session.ClarifyingQuestions++;
                if (session.ClarifyingQuestions > 3)
                {
                    return await EscalateAsync(session, settings, "More than 3 clarifying questions", false, "I will have the front desk finish this booking for you. Please hold.", "Appointment", cancellationToken);
                }

                action = "Asked for preferred time";
                reply = $"When would you like to see {doctor!.Name}? Please say a day and time such as Monday at 10 AM.";
            }
            else if (session.AfterHours && session.PreferredTime.Value.Date == DateTime.Today)
            {
                return await QueueCallbackAsync(session, settings, "After-hours booking request", false, cancellationToken);
            }
            else
            {
                try
                {
                    booked = await BookFromSessionAsync(session, doctor!, cancellationToken);
                    action = $"Booked for {booked.ScheduledAt:h:mm tt} with {booked.DoctorName}";
                    var confirmExtra = string.IsNullOrWhiteSpace(booked.PatientName) ? "" : $" I have {booked.PatientName} with {booked.DoctorName} on {booked.ScheduledAt:dddd, h:mm tt}.";
                    reply = $"Your appointment is confirmed.{confirmExtra} A confirmation has been sent to the doctor" +
                            (session.PatientId.HasValue ? " and to the patient email on file." : ".") +
                            (string.IsNullOrWhiteSpace(session.Symptoms) ? "" : $" I noted symptoms: {session.Symptoms}.");
                    ended = true;
                    session.Outcome = "Contained";
                    session.Phase = "Ended";
                    await AppendPatientNoteAsync(session, action, cancellationToken);
                }
                catch (Exception ex)
                {
                    session.PreferredTime = null;
                    session.ClarifyingQuestions++;
                    action = $"Booking failed: {ex.Message}";
                    reply = $"I could not book that slot. {ex.Message} Please suggest another time.";
                }
            }
        }
        else
        {
            label = "Query";
            reply = await AnswerQueryAsync(transcript, cancellationToken);
            action = "Answered clinic query";
            session.Phase = "Acting";
        }

        session.Turns.Add(new VoiceChatTurn("agent", reply, label));
        session.Ended = ended;
        CallLogDto? log = null;
        if (ended)
        {
            log = await EnsureLogAsync(session, intent.Summary, action, label, cancellationToken);
        }

        return await ToSessionDtoAsync(session, settings, reply, label, intent.Summary, action, log, booked, cancellationToken);
    }

    private async Task<AppointmentDto> BookFromSessionAsync(VoiceSession session, Doctor doctor, CancellationToken cancellationToken)
    {
        var patient = await FindOrCreatePatientAsync(session.CallerName, session.CallerPhone, cancellationToken);
        session.PatientId = patient.Id;
        var time = session.PreferredTime ?? NextOpenSlot(doctor);
        var notes = "Booked via AI voice agent";
        if (!string.IsNullOrWhiteSpace(session.AppointmentType))
        {
            notes += $"; type: {session.AppointmentType}";
        }

        if (!string.IsNullOrWhiteSpace(session.Symptoms))
        {
            notes += $"; symptoms: {session.Symptoms}";
        }

        var booked = await _appointments.BookAsync(
            new BookAppointmentRequest(patient.Id, doctor.Id, time, notes),
            pushDashboard: false,
            cancellationToken);

        if (!string.IsNullOrWhiteSpace(patient.Email))
        {
            await _email.SendAsync(
                patient.Email,
                "Your Anuj Clinic appointment",
                $"Hello {patient.Name},\n\nYour appointment with {booked.DoctorName} is confirmed for {booked.ScheduledAt:dddd, d MMM yyyy, h:mm tt}.\nPlease arrive 15 minutes early with ID and any reports.\n\nRegards,\n{ (await _db.AiSettings.FirstAsync(cancellationToken)).AgentName }",
                cancellationToken);
        }

        return booked;
    }

    private async Task IdentifyCallerAsync(VoiceSession session, CancellationToken cancellationToken)
    {
        if (session.PatientId.HasValue)
        {
            var known = await _db.Patients.FirstOrDefaultAsync(p => p.Id == session.PatientId, cancellationToken);
            if (known is not null)
            {
                session.IsVip = known.IsVip;
                session.PatientUhid = known.Uhid;
                if (session.CallerName is "Unknown" or "")
                {
                    session.CallerName = known.Name;
                }
            }

            return;
        }

        var uhid = session.PatientUhid;
        var patient = await _db.Patients.FirstOrDefaultAsync(p =>
            (!string.IsNullOrEmpty(uhid) && p.Uhid == uhid)
            || p.Contact == session.CallerPhone
            || (!string.IsNullOrWhiteSpace(session.CallerName) && session.CallerName != "Unknown" && p.Name == session.CallerName), cancellationToken);

        if (patient is null)
        {
            return;
        }

        session.PatientId = patient.Id;
        session.PatientUhid = patient.Uhid;
        session.IsVip = patient.IsVip;
        if (session.CallerName is "Unknown" or "")
        {
            session.CallerName = patient.Name;
        }
    }

    private async Task<bool> IsClinicOpenAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var doctors = await ActiveDoctorsAsync(cancellationToken);
        return doctors.Any(d => d.Schedules.Any(s => s.DayOfWeek == now.DayOfWeek && now.TimeOfDay >= s.StartTime && now.TimeOfDay < s.EndTime));
    }

    private async Task<VoiceSessionDto> EscalateAsync(
        VoiceSession session,
        AiSettings settings,
        string reason,
        bool priority,
        string reply,
        string intent,
        CancellationToken cancellationToken)
    {
        session.EscalationReason = reason;
        session.Phase = "Escalating";
        session.Outcome = "Escalated";
        session.TransferType = string.IsNullOrWhiteSpace(settings.TransferNumber) ? "Callback" : "Warm";
        session.Ended = true;
        session.ConsentGiven = session.ConsentGiven || true;
        if (session.TransferType == "Callback")
        {
            reply = $"{reply} No desk line is configured, so I have queued a priority callback.";
            await SaveCallbackAsync(session, reason, priority, cancellationToken);
        }

        session.Turns.Add(new VoiceChatTurn("agent", reply, "Escalated"));
        var log = await EnsureLogAsync(session, reason, $"Escalated: {reason}", intent, cancellationToken);
        await _hub.Clients.All.SendAsync("TransferRequested", new
        {
            session.CallerName,
            session.CallerPhone,
            reason,
            session.TransferType,
            priority,
            transcript = string.Join(" | ", session.Turns.Select(t => $"{t.Role}: {t.Text}")),
            session.IsVip
        }, cancellationToken);
        return await ToSessionDtoAsync(session, settings, reply, "Escalated", reason, log.ActionTaken, log, null, cancellationToken);
    }

    private async Task<VoiceSessionDto> QueueCallbackAsync(
        VoiceSession session,
        AiSettings settings,
        string summary,
        bool priority,
        CancellationToken cancellationToken)
    {
        session.Outcome = "Callback";
        session.TransferType = "Callback";
        session.Phase = "Ended";
        session.Ended = true;
        session.PreferredTime ??= DateTime.Now.AddHours(1);
        await SaveCallbackAsync(session, summary, priority, cancellationToken);
        var reply = priority
            ? "I have added you to the priority callback queue. A clinician will call you back as soon as possible."
            : $"I have scheduled a callback for {session.PreferredTime:dddd, h:mm tt}. Someone from the clinic will phone you.";
        session.Turns.Add(new VoiceChatTurn("agent", reply, "Callback"));
        var log = await EnsureLogAsync(session, summary, "Queued callback", "Callback", cancellationToken);
        await _hub.Clients.All.SendAsync("CallbackQueued", new { session.CallerName, session.CallerPhone, session.PreferredTime, priority, summary }, cancellationToken);
        return await ToSessionDtoAsync(session, settings, reply, "Callback", summary, "Queued callback", log, null, cancellationToken);
    }

    private async Task SaveCallbackAsync(VoiceSession session, string reason, bool priority, CancellationToken cancellationToken)
    {
        _db.CallCallbacks.Add(new CallCallback
        {
            CallerName = session.CallerName,
            CallerPhone = session.CallerPhone,
            Reason = reason,
            Summary = string.Join(" | ", session.Turns.TakeLast(6).Select(t => $"{t.Role}: {t.Text}")),
            PreferredTime = session.PreferredTime,
            Status = "Queued",
            Priority = priority || session.IsVip,
            CreatedAt = DateTime.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<(string Reply, string Action, bool Ended)> HandleBillingAsync(VoiceSession session, string transcript, CancellationToken cancellationToken)
    {
        if (LooksLikeBillingDispute(transcript))
        {
            return ("Billing disputes need a person. I will transfer you.", "Escalate billing dispute", false);
        }

        if (session.PatientId is null)
        {
            session.ClarifyingQuestions++;
            return ("I can check your balance. Please say your name or UHID, such as ANJ-000001.", "Asked for patient identity", false);
        }

        var invoices = await _finance.ListInvoicesAsync(session.PatientId, cancellationToken);
        var balance = invoices.Sum(i => i.Balance);
        return ($"Your current outstanding balance is rupees {balance:0}. For a refund or insurance negotiation I can transfer you to billing.", $"Quoted balance {balance:0}", false);
    }

    private async Task<(string Reply, string Action, bool Ended, AppointmentDto? Booked)> HandleRescheduleAsync(
        VoiceSession session,
        Doctor? _,
        CancellationToken cancellationToken)
    {
        if (session.PatientId is null)
        {
            session.ClarifyingQuestions++;
            return ("I can reschedule. Please confirm the patient name or UHID on the file.", "Asked for patient identity", false, null);
        }

        var existing = await _db.Appointments
            .Include(a => a.Doctor)
            .Where(a => a.PatientId == session.PatientId && a.Status == "Scheduled" && a.ScheduledAt >= DateTime.Now)
            .OrderBy(a => a.ScheduledAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            return ("I could not find an upcoming appointment to move. I can book a new one instead.", "No upcoming appointment", false, null);
        }

        if (session.PreferredTime is null)
        {
            session.ClarifyingQuestions++;
            return ($"I have you with {existing.Doctor.Name} on {existing.ScheduledAt:dddd, h:mm tt}. What new day and time would you like?", "Asked for new time", false, null);
        }

        try
        {
            var moved = await _appointments.RescheduleAsync(existing.Id, session.PreferredTime.Value, cancellationToken);
            session.Outcome = "Contained";
            session.Phase = "Ended";
            await AppendPatientNoteAsync(session, $"Rescheduled to {moved.ScheduledAt:g}", cancellationToken);
            return ($"Done. Your appointment with {moved.DoctorName} is now {moved.ScheduledAt:dddd, h:mm tt}. A confirmation email has been sent.", $"Rescheduled to {moved.ScheduledAt:h:mm tt}", true, moved);
        }
        catch (Exception ex)
        {
            session.PreferredTime = null;
            return ($"I could not move that slot. {ex.Message} Please suggest another time.", ex.Message, false, null);
        }
    }

    private async Task<(string Reply, string Action, bool Ended)> HandleCancelAsync(VoiceSession session, CancellationToken cancellationToken)
    {
        if (session.PatientId is null)
        {
            session.ClarifyingQuestions++;
            return ("I can cancel. Please confirm the patient name or UHID.", "Asked for patient identity", false);
        }

        var existing = await _db.Appointments
            .Include(a => a.Doctor)
            .Where(a => a.PatientId == session.PatientId && a.Status == "Scheduled" && a.ScheduledAt >= DateTime.Now)
            .OrderBy(a => a.ScheduledAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (existing is null)
        {
            return ("I could not find an upcoming appointment to cancel.", "No upcoming appointment", false);
        }

        var cancelled = await _appointments.UpdateStatusAsync(existing.Id, "Cancelled", cancellationToken);
        session.Outcome = "Contained";
        session.Phase = "Ended";
        await AppendPatientNoteAsync(session, $"Cancelled appointment {cancelled.Id}", cancellationToken);
        return ($"Your appointment with {cancelled.DoctorName} on {cancelled.ScheduledAt:dddd, h:mm tt} is cancelled.", "Cancelled upcoming appointment", true);
    }

    private async Task AppendPatientNoteAsync(VoiceSession session, string note, CancellationToken cancellationToken)
    {
        if (session.PatientId is null)
        {
            return;
        }

        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == session.PatientId, cancellationToken);
        if (patient is null)
        {
            return;
        }

        patient.Notes = string.IsNullOrWhiteSpace(patient.Notes)
            ? $"[{DateTime.Now:g}] Voice: {note}"
            : $"{patient.Notes}\n[{DateTime.Now:g}] Voice: {note}";
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task<string> AnswerQueryAsync(string transcript, CancellationToken cancellationToken)
    {
        var text = transcript.ToLowerInvariant();
        var doctors = await ActiveDoctorsAsync(cancellationToken);
        if (text.Contains("hour") || text.Contains("timing") || text.Contains("open") || text.Contains("visit"))
        {
            if (doctors.Count == 0)
            {
                return "Clinic hours are Monday to Saturday, 9 AM to 6 PM.";
            }

            var lines = doctors.Select(d =>
            {
                var hours = d.Schedules
                    .OrderBy(s => s.DayOfWeek)
                    .Select(s => $"{s.DayOfWeek} {s.StartTime:hh\\:mm}-{s.EndTime:hh\\:mm}");
                return $"{d.Name}: {string.Join(", ", hours)}";
            });
            return "Our visiting hours are: " + string.Join(". ", lines);
        }

        if (text.Contains("doctor") || text.Contains("special"))
        {
            return doctors.Count == 0
                ? "I do not have an active doctor listed right now."
                : "Our doctors are " + string.Join(", ", doctors.Select(d => $"{d.Name}, {d.Specialization}"));
        }

        if (text.Contains("address") || text.Contains("where") || text.Contains("location"))
        {
            return "You have reached Anuj Clinic. Please visit the reception desk or book an appointment on this line.";
        }

        if (text.Contains("prep") || text.Contains("bring") || text.Contains("fasting") || text.Contains("instruction"))
        {
            return "Please arrive 15 minutes early. Bring a photo ID, your UHID if you have one, and any recent reports or insurance card.";
        }

        return "Thank you. I have noted your query and our staff will follow up if needed.";
    }

    private async Task<CallLogDto> EnsureLogAsync(VoiceSession session, string summary, string action, string intent, CancellationToken cancellationToken)
    {
        var transcript = string.Join(" | ", session.Turns.Select(t => $"{t.Role}: {t.Text}"));
        if (session.Logged)
        {
            return ToLogDto(0, session, summary, action, intent, transcript);
        }

        if (string.IsNullOrWhiteSpace(session.Outcome))
        {
            session.Outcome = session.TransferType == "None" ? "Contained" : session.Outcome;
        }

        var callLog = new CallLog
        {
            CallerName = session.CallerName,
            CallerPhone = session.CallerPhone,
            Summary = string.IsNullOrWhiteSpace(summary) ? "Voice call" : summary,
            ActionTaken = action,
            Intent = intent,
            Transcript = transcript,
            Timestamp = DateTime.UtcNow,
            Outcome = string.IsNullOrWhiteSpace(session.Outcome) ? "Contained" : session.Outcome,
            EscalationReason = session.EscalationReason,
            TransferType = session.TransferType,
            Confidence = session.LastConfidence,
            Sentiment = session.Sentiment,
            ConsentGiven = session.ConsentGiven,
            PatientId = session.PatientId
        };
        _db.CallLogs.Add(callLog);
        await _db.SaveChangesAsync(cancellationToken);
        session.Logged = true;

        var dto = ToLogDto(callLog.Id, session, callLog.Summary, callLog.ActionTaken, callLog.Intent, callLog.Transcript);
        await _hub.Clients.All.SendAsync("CallSummaryAdded", dto, cancellationToken);
        return dto;
    }

    private static CallLogDto ToLogDto(int id, VoiceSession session, string summary, string action, string intent, string transcript) =>
        new(
            id,
            session.CallerName,
            session.CallerPhone,
            summary,
            action,
            intent,
            transcript,
            DateTime.UtcNow,
            string.IsNullOrWhiteSpace(session.Outcome) ? "Contained" : session.Outcome,
            session.EscalationReason,
            session.TransferType,
            session.LastConfidence,
            session.Sentiment,
            session.ConsentGiven);

    private async Task<VoiceSessionDto> ToSessionDtoAsync(
        VoiceSession session,
        AiSettings settings,
        string reply,
        string intent,
        string summary,
        string action,
        CallLogDto? log,
        AppointmentDto? booked,
        CancellationToken cancellationToken)
    {
        var language = settings.Language.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault() ?? "en-IN";
        var audio = await _ai.SynthesizeBase64Async(reply, language, settings.VoiceSpeaker, cancellationToken);
        session.ReplyAudio = TryDecodeAudio(audio);
        return new VoiceSessionDto(
            session.Id,
            settings.AgentName,
            settings.WelcomeMessage,
            reply,
            intent,
            summary,
            action,
            session.Ended,
            log,
            booked,
            audio,
            session.Turns.Select(t => new VoiceChatTurnDto(t.Role, t.Text, t.Intent)).ToList(),
            session.Phase,
            session.LastConfidence,
            session.Outcome,
            session.EscalationReason,
            session.TransferType,
            string.IsNullOrWhiteSpace(settings.TransferNumber) ? null : settings.TransferNumber,
            session.AfterHours,
            session.ConsentGiven,
            session.ClarifyingQuestions,
            session.IsVip);
    }

    private static byte[]? TryDecodeAudio(string? audio)
    {
        if (string.IsNullOrWhiteSpace(audio))
        {
            return null;
        }

        try
        {
            return Convert.FromBase64String(audio);
        }
        catch (FormatException)
        {
            return null;
        }
    }

    private async Task<List<Doctor>> ActiveDoctorsAsync(CancellationToken cancellationToken) =>
        await _db.Doctors.Include(d => d.Schedules).Where(d => d.IsActive).OrderBy(d => d.Name).ToListAsync(cancellationToken);

    private async Task<Doctor?> FindDoctorFromTextAsync(string transcript, CancellationToken cancellationToken)
    {
        var doctors = await ActiveDoctorsAsync(cancellationToken);
        return doctors.FirstOrDefault(d =>
            transcript.Contains(d.Name, StringComparison.OrdinalIgnoreCase)
            || transcript.Contains(d.Name.Replace("Dr.", "", StringComparison.OrdinalIgnoreCase).Trim(), StringComparison.OrdinalIgnoreCase)
            || transcript.Contains(d.Name.Split(' ').Last(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Patient> FindOrCreatePatientAsync(string callerName, string callerPhone, CancellationToken cancellationToken)
    {
        var name = string.IsNullOrWhiteSpace(callerName) ? "Unknown Caller" : callerName;
        var phone = callerPhone ?? string.Empty;
        var existing = await _db.Patients.FirstOrDefaultAsync(p =>
            (!string.IsNullOrEmpty(phone) && p.Contact == phone) || p.Name == name, cancellationToken);
        if (existing is not null)
        {
            return existing;
        }

        var patient = new Patient
        {
            Name = name,
            Contact = phone,
            Notes = "Created from inbound voice call"
        };
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync(cancellationToken);
        patient.Uhid = $"ANJ-{patient.Id:D6}";
        await _db.SaveChangesAsync(cancellationToken);
        return patient;
    }

    private static void FillSlots(VoiceSession session, string transcript)
    {
        var uhid = System.Text.RegularExpressions.Regex.Match(transcript, @"ANJ-\d{4,}", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (uhid.Success)
        {
            session.PatientUhid = uhid.Value.ToUpperInvariant();
        }

        var types = new[] { "consult", "consultation", "follow up", "follow-up", "checkup", "check-up", "lab", "scan", "vaccine" };
        var lower = transcript.ToLowerInvariant();
        session.AppointmentType ??= types.FirstOrDefault(t => lower.Contains(t));

        if (lower.Contains("pain") || lower.Contains("fever") || lower.Contains("cough") || lower.Contains("symptom") || lower.Contains("dizzy"))
        {
            session.Symptoms = transcript;
        }
    }

    private static bool LooksLikeHumanRequest(string text)
    {
        var value = text.ToLowerInvariant();
        if (value.Contains("book") || value.Contains("appoint") || value.Contains("slot"))
        {
            return false;
        }

        return value.Contains("speak to a person") || value.Contains("speak to someone") || value.Contains("speak to an agent")
            || value.Contains("speak to a human") || value.Contains("talk to a person") || value.Contains("talk to someone")
            || value.Contains("talk to an agent") || value.Contains("talk to a human") || value.Contains("speak to a doctor")
            || value.Contains("talk to a doctor") || value.Contains("i want a person") || value.Contains("real person")
            || value.Contains("transfer me") || value.Contains("connect me") || value.Contains("human please")
            || (value.Contains("agent") && (value.Contains("speak") || value.Contains("talk") || value.Contains("real")));
    }

    private static bool LooksLikeEmergency(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("emergency") || value.Contains("chest pain") || value.Contains("suicid")
            || value.Contains("can't breathe") || value.Contains("cannot breathe") || value.Contains("unconscious")
            || value.Contains("heart attack") || value.Contains("stroke") || value.Contains("severe bleeding")
            || value.Contains("not breathing");
    }

    private static bool LooksLikeBillingDispute(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("dispute") || value.Contains("wrong charge") || value.Contains("insurance negotiation")
            || value.Contains("contest the bill") || value.Contains("overcharged");
    }

    private static bool LooksLikeBilling(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("bill") || value.Contains("invoice") || value.Contains("balance") || value.Contains("payment due");
    }

    private static bool LooksLikeReschedule(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("reschedul") || value.Contains("change my appointment") || value.Contains("move my appointment");
    }

    private static bool LooksLikeCancel(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("cancel my appointment") || value.Contains("cancel the appointment");
    }

    private static bool LooksLikeCallback(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("callback") || value.Contains("call me back") || value.Contains("call back");
    }

    private static bool LooksLikeConsentRefusal(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("do not record") || value.Contains("don't record") || value.Contains("i do not agree")
            || value.Contains("no consent") || value.Contains("i don't agree");
    }

    private static string GuessSentiment(string text)
    {
        var value = text.ToLowerInvariant();
        if (value.Contains("thank") || value.Contains("great") || value.Contains("perfect"))
        {
            return "Positive";
        }

        if (value.Contains("angry") || value.Contains("worst") || value.Contains("terrible") || value.Contains("dispute") || value.Contains("suicid"))
        {
            return "Negative";
        }

        return "Neutral";
    }

    private static string DigitsOnly(string value) => new(value.Where(char.IsDigit).ToArray());

    private static bool LooksLikeBooking(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("appoint") || value.Contains("book") || value.Contains("slot") || value.Contains("doctor");
    }

    private static bool LooksLikeSpam(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("lottery") || value.Contains("prize") || value.Contains("congratulations") || value.Contains("credit card offer");
    }

    private static bool LooksLikeGoodbye(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("bye") || value.Contains("thank you") || value.Contains("thanks") || value.Contains("that's all") || value.Contains("hang up");
    }

    private static DateTime? GuessTimeFromText(string transcript)
    {
        var text = transcript.ToLowerInvariant();
        var day = ResolveDayFromText(text, out var namedDay);

        DateTime? when = null;
        for (var hour = 8; hour <= 20; hour++)
        {
            var twelve = hour > 12 ? hour - 12 : hour;
            if (text.Contains($"{hour} pm") || text.Contains($"{twelve} pm") || text.Contains($"{twelve}pm"))
            {
                var h = twelve == 12 ? 12 : twelve + 12;
                when = new DateTime(day.Year, day.Month, day.Day, h >= 24 ? 12 : h, 0, 0);
                break;
            }

            if (text.Contains($"{hour} am") || text.Contains($"{twelve} am") || text.Contains($"{twelve}am"))
            {
                when = new DateTime(day.Year, day.Month, day.Day, twelve == 12 ? 0 : twelve, 0, 0);
                break;
            }
        }

        if (when is null && text.Contains("morning"))
        {
            when = day.AddHours(10);
        }
        else if (when is null && (text.Contains("evening") || text.Contains("afternoon")))
        {
            when = day.AddHours(17);
        }

        if (when is null)
        {
            return null;
        }

        if (when.Value < DateTime.Now)
        {
            when = when.Value.AddDays(namedDay ? 7 : 1);
        }

        return when;
    }

    private static DateTime ResolveDayFromText(string text, out bool namedDay)
    {
        namedDay = false;
        if (text.Contains("tomorrow"))
        {
            namedDay = true;
            return DateTime.Today.AddDays(1);
        }

        if (text.Contains("today"))
        {
            namedDay = true;
            return DateTime.Today;
        }

        foreach (DayOfWeek dow in Enum.GetValues<DayOfWeek>())
        {
            if (!text.Contains(dow.ToString().ToLowerInvariant()))
            {
                continue;
            }

            namedDay = true;
            var delta = ((int)dow - (int)DateTime.Today.DayOfWeek + 7) % 7;
            return DateTime.Today.AddDays(delta);
        }

        return DateTime.Today;
    }

    private static DateTime NextOpenSlot(Doctor doctor)
    {
        var cursor = DateTime.Now.AddHours(1);
        cursor = new DateTime(cursor.Year, cursor.Month, cursor.Day, cursor.Hour, 0, 0);
        for (var i = 0; i < 14 * 24; i++)
        {
            var day = cursor.DayOfWeek;
            var time = cursor.TimeOfDay;
            if (doctor.Schedules.Any(s => s.DayOfWeek == day && time >= s.StartTime && time < s.EndTime))
            {
                return cursor;
            }

            cursor = cursor.AddHours(1);
        }

        return DateTime.Now.AddDays(1).Date.AddHours(17);
    }
}
