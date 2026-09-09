using System.Text.RegularExpressions;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;

namespace AiVoicePortal.Api.Services;

public static class CallLogDetails
{
    public static CallLogDto ToDto(CallLog call, Appointment? appointment = null, CallCallback? callback = null, IReadOnlyList<Patient>? patients = null)
    {
        var status = callback?.Status ?? "";
        var name = DisplayName(call.CallerName);
        if ((name == "Unknown caller" || HasIndic(name)) && !string.IsNullOrWhiteSpace(appointment?.Patient?.Name) && !HasIndic(appointment.Patient.Name))
        {
            name = appointment.Patient.Name.Trim();
        }

        var phone = FormatPhone(FirstPhone(call.CallerPhone, appointment?.Patient?.Contact, PatientContact(call, name, patients), ExtractPhone(CallText(call))));

        if (name == "Unknown caller")
        {
            var guessed = ExtractCallerName(CallText(call));
            if (!string.IsNullOrWhiteSpace(guessed))
            {
                name = guessed;
            }
        }

        var doctor = appointment?.Doctor?.Name ?? ExtractBookedDoctor(CallText(call));
        var when = appointment?.ScheduledAt;
        var summary = BuildSummary(call, name, doctor, when);
        var completed = status.Equals("Completed", StringComparison.OrdinalIgnoreCase);
        var unansweredForward = !completed && appointment is null && IsUnansweredForward(call);
        var needsContact = !completed && (unansweredForward || IsCallbackRequested(call, appointment, callback));
        var callbackStatus = completed
            ? "Completed"
            : !needsContact
                ? ""
                : unansweredForward
                    ? "Required"
                    : string.IsNullOrWhiteSpace(status) ? "Queued" : status;

        return new(
            call.Id,
            name,
            phone,
            summary,
            call.ActionTaken,
            call.Intent,
            call.Transcript,
            call.Timestamp,
            call.Outcome,
            call.EscalationReason,
            call.TransferType,
            call.Confidence,
            call.Sentiment,
            call.ConsentGiven,
            appointment?.Id,
            doctor,
            when,
            needsContact,
            callbackStatus);
    }

    public static string BuildSummary(CallLog call, string name, string doctor, DateTime? when)
    {
        if (!IsGenericSummary(call.Summary))
        {
            return call.Summary.Trim();
        }

        if (!string.IsNullOrWhiteSpace(doctor) && when.HasValue)
        {
            return $"{name} booked with {doctor} for {when:ddd d MMM, h:mm tt} IST.";
        }

        if (!string.IsNullOrWhiteSpace(doctor))
        {
            return $"{name} booked with {doctor}.";
        }

        var fromTranscript = TruncateSummary(call.Transcript);
        if (!string.IsNullOrWhiteSpace(fromTranscript) && !fromTranscript.StartsWith("Sarvam Voicebot", StringComparison.OrdinalIgnoreCase))
        {
            return fromTranscript;
        }

        return "Call completed. No appointment was booked.";
    }

    public static bool HasIndic(string? text) =>
        !string.IsNullOrWhiteSpace(text) && text.Any(c => c is >= '\u0900' and <= '\u097F');

    public static bool IsGenericSummary(string? summary) =>
        string.IsNullOrWhiteSpace(summary)
        || summary.StartsWith("Sarvam Voicebot", StringComparison.OrdinalIgnoreCase)
        || summary.Equals("No summary captured", StringComparison.OrdinalIgnoreCase)
        || HasIndic(summary);

    public static string ExtractPhone(string text)
    {
        var match = Regex.Match(text ?? "", @"(?:\+91[\s-]?)?[6-9]\d{9}");
        return match.Success ? DisplayPhone(match.Value) : "";
    }

    public static string ExtractCallerName(string text)
    {
        foreach (var pattern in new[]
        {
            @"my name is ([A-Za-z][A-Za-z .']{1,40})",
            @"i am ([A-Za-z][A-Za-z .']{1,40})",
            @"this is ([A-Za-z][A-Za-z .']{1,40})",
            @"mera naam ([A-Za-z\u0900-\u097F][A-Za-z\u0900-\u097F .']{1,40})"
        })
        {
            var match = Regex.Match(text ?? "", pattern, RegexOptions.IgnoreCase);
            if (match.Success)
            {
                var name = match.Groups[1].Value.Trim().TrimEnd('.', ',', ' ');
                if (name.Length >= 2 && !name.Equals("speaking", StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }

        return "";
    }

    public static string ExtractBookedDoctor(string text)
    {
        var match = Regex.Match(text ?? "", @"booked with (Dr\.?\s*[A-Z][a-zA-Z]+)", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : "";
    }

    public static int? BookedAppointmentId(CallLog call)
    {
        const string prefix = "sarvam-book:";
        if (!string.IsNullOrWhiteSpace(call.ExternalId)
            && call.ExternalId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && int.TryParse(call.ExternalId[prefix.Length..], out var id))
        {
            return id;
        }

        return null;
    }

    public static string FormatPhone(string? phone)
    {
        var raw = DisplayPhone(phone);
        var digits = Last10(raw);
        if (digits.Length == 10)
        {
            return $"+91 {digits[..5]} {digits[5..]}";
        }

        return raw;
    }

    private static string FirstPhone(params string?[] values)
    {
        foreach (var value in values)
        {
            var phone = DisplayPhone(value);
            if (!string.IsNullOrWhiteSpace(phone))
            {
                return phone;
            }
        }

        return "";
    }

    private static string? PatientContact(CallLog call, string name, IReadOnlyList<Patient>? patients)
    {
        if (patients is null || patients.Count == 0)
        {
            return null;
        }

        if (call.PatientId is int id)
        {
            var byId = patients.FirstOrDefault(p => p.Id == id);
            if (!string.IsNullOrWhiteSpace(byId?.Contact))
            {
                return byId.Contact;
            }
        }

        if (name == "Unknown caller")
        {
            return null;
        }

        var matches = patients
            .Where(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(p.Contact))
            .ToList();
        return matches.Count == 1 ? matches[0].Contact : null;
    }

    private static string TruncateSummary(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var text = Regex.Replace(value, @"\s+", " ").Trim();
        return text.Length <= 280 ? text : text[..277] + "...";
    }

    public static bool IsForwarded(CallLog call)
    {
        if (LooksBooked(call))
        {
            return false;
        }

        var outcome = call.Outcome ?? "";
        if (outcome.Equals("Escalated", StringComparison.OrdinalIgnoreCase)
            || outcome.Equals("Forwarded", StringComparison.OrdinalIgnoreCase)
            || outcome.Equals("Transfer", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var transfer = call.TransferType ?? "";
        if (!string.IsNullOrWhiteSpace(transfer)
            && !transfer.Equals("None", StringComparison.OrdinalIgnoreCase)
            && !transfer.Equals("Callback", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return LooksForwarded(CallText(call));
    }

    public static bool IsUnansweredForward(CallLog call)
    {
        if (!IsForwarded(call) && !LooksForwarded(CallText(call)))
        {
            return false;
        }

        return LooksUnanswered(CallText(call))
            || (call.Outcome ?? "").Equals("Failed", StringComparison.OrdinalIgnoreCase)
            || (call.TransferType ?? "").Equals("Callback", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCallbackRequested(CallLog call, Appointment? appointment, CallCallback? callback)
    {
        if (appointment is not null)
        {
            return false;
        }

        if (IsUnansweredForward(call))
        {
            return true;
        }

        if (callback is not null && callback.Status.Equals("Queued", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var text = CallText(call);
        return WantsCallback(text) && AiDidNotResolve(call, text);
    }

    public static bool WantsCallback(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("callback")
            || value.Contains("call back")
            || value.Contains("call me back")
            || value.Contains("call us back")
            || value.Contains("wapas call")
            || value.Contains("phone karo")
            || value.Contains("call karo")
            || value.Contains("call later")
            || value.Contains("speak to a person")
            || value.Contains("talk to a person")
            || value.Contains("talk to someone")
            || value.Contains("human agent");
    }

    public static bool AiDidNotResolve(CallLog call, string text)
    {
        var value = text.ToLowerInvariant();
        var outcome = call.Outcome ?? "";
        return outcome.Equals("Failed", StringComparison.OrdinalIgnoreCase)
            || outcome.Equals("Escalated", StringComparison.OrdinalIgnoreCase)
            || call.Intent.Equals("Human", StringComparison.OrdinalIgnoreCase)
            || value.Contains("no_response")
            || value.Contains("no response")
            || value.Contains("unanswered")
            || value.Contains("no_answer")
            || value.Contains("not_connected")
            || value.Contains("did not answer")
            || value.Contains("didn't answer")
            || value.Contains("could not answer")
            || value.Contains("couldn't answer")
            || value.Contains("could not help")
            || value.Contains("couldn't help")
            || value.Contains("unable to")
            || value.Contains("not able to")
            || value.Contains("asked for a person")
            || value.Contains("needs a person")
            || value.Contains("voicebot failed")
            || value.Contains("voicebot missed")
            || value.Contains("voicebot unanswered");
    }

    public static bool LooksForwarded(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("forward")
            || value.Contains("transfer")
            || value.Contains("transferred")
            || value.Contains("connecting you")
            || value.Contains("connect you")
            || value.Contains("warm transfer")
            || value.Contains("handoff")
            || value.Contains("hand off");
    }

    public static bool LooksUnanswered(string text)
    {
        var value = text.ToLowerInvariant();
        return value.Contains("no_answer")
            || value.Contains("no-answer")
            || value.Contains("no answer")
            || value.Contains("unanswered")
            || value.Contains("not answered")
            || value.Contains("did not answer")
            || value.Contains("didn't answer")
            || value.Contains("did not pick")
            || value.Contains("didn't pick")
            || value.Contains("no_response")
            || value.Contains("no response")
            || value.Contains("not_connected")
            || value.Contains("not connected")
            || value.Contains("transfer_failed")
            || value.Contains("transfer failed")
            || value.Contains("nobody picked")
            || value.Contains("no one picked")
            || value.Contains("staff unavailable");
    }

    public static bool LooksBooked(CallLog call)
    {
        var text = $"{call.ActionTaken} {call.Summary} {call.Outcome}";
        return text.Contains("booked", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("could not book", StringComparison.OrdinalIgnoreCase)
            && !text.Contains("couldn't book", StringComparison.OrdinalIgnoreCase);
    }

    public static string CallText(CallLog call) =>
        $"{call.Summary} {call.Transcript} {call.ActionTaken} {call.Intent} {call.Outcome} {call.EscalationReason} {call.TransferType}";

    public static CallCallback? FindCallback(CallLog call, IReadOnlyList<CallCallback> callbacks)
    {
        var phone = Last10(DisplayPhone(call.CallerPhone));
        return callbacks
            .OrderByDescending(c => c.CreatedAt)
            .FirstOrDefault(c =>
                (phone.Length >= 10 && Last10(c.CallerPhone) == phone)
                || (!string.Equals(call.CallerName, "Unknown", StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(call.CallerName)
                    && c.CallerName.Equals(call.CallerName.Trim(), StringComparison.OrdinalIgnoreCase)
                    && (c.CreatedAt - call.Timestamp).Duration() <= TimeSpan.FromHours(24)));
    }

    public static Appointment? FindAppointment(CallLog call, IReadOnlyList<Appointment> appointments)
    {
        var bookedId = BookedAppointmentId(call);
        if (bookedId is int appointmentId)
        {
            var exact = appointments.FirstOrDefault(a => a.Id == appointmentId);
            if (exact is not null)
            {
                return exact;
            }
        }

        Appointment? best = null;
        var bestScore = 0;
        foreach (var appointment in appointments.Where(a => a.Status != "Cancelled"))
        {
            var score = Score(call, appointment);
            if (score > bestScore)
            {
                bestScore = score;
                best = appointment;
            }
        }

        if (bestScore >= 80)
        {
            return best;
        }

        var recent = appointments
            .Where(a =>
                (a.Notes.Contains("Voicebot", StringComparison.OrdinalIgnoreCase)
                    || a.Notes.Contains("Sarvam", StringComparison.OrdinalIgnoreCase))
                && (a.CreatedAt - call.Timestamp).Duration() <= TimeSpan.FromHours(3))
            .OrderByDescending(a => a.CreatedAt)
            .ToList();
        if (recent.Count == 1)
        {
            var phone = Last10(DisplayPhone(call.CallerPhone));
            var patientPhone = Last10(recent[0].Patient?.Contact);
            if (phone.Length < 10 || patientPhone.Length < 10 || phone == patientPhone)
            {
                return recent[0];
            }
        }

        return null;
    }

    public static string DisplayName(string? name) =>
        string.IsNullOrWhiteSpace(name) || name.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
            ? "Unknown caller"
            : name.Trim();

    public static string DisplayPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)
            || phone.Contains("identifier", StringComparison.OrdinalIgnoreCase)
            || phone.Equals("unknown", StringComparison.OrdinalIgnoreCase))
        {
            return "";
        }

        return phone.Trim();
    }

    public static string Last10(string? value)
    {
        var digits = Regex.Replace(value ?? "", @"\D", "");
        return digits.Length >= 10 ? digits[^10..] : digits;
    }

    private static int Score(CallLog call, Appointment appointment)
    {
        var score = 0;
        if (call.PatientId is int patientId && appointment.PatientId == patientId)
        {
            score += 100;
        }

        var callPhone = Last10(DisplayPhone(call.CallerPhone));
        var patientPhone = Last10(appointment.Patient?.Contact);
        if (callPhone.Length >= 10 && callPhone == patientPhone)
        {
            score += 80;
        }

        if (!string.Equals(call.CallerName, "Unknown", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(call.CallerName)
            && appointment.Patient is not null
            && appointment.Patient.Name.Equals(call.CallerName.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            score += 40;
        }

        var created = appointment.CreatedAt;
        var delta = (created - call.Timestamp).Duration();
        if (delta <= TimeSpan.FromHours(3))
        {
            score += 30;
        }
        else if (delta <= TimeSpan.FromHours(24))
        {
            score += 10;
        }

        if (appointment.Notes.Contains("Voicebot", StringComparison.OrdinalIgnoreCase)
            || appointment.Notes.Contains("Sarvam", StringComparison.OrdinalIgnoreCase))
        {
            score += 20;
        }

        return score;
    }
}
