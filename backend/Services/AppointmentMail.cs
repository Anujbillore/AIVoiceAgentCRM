using System.Security.Cryptography;
using System.Text;
using AiVoicePortal.Api.Models;

namespace AiVoicePortal.Api.Services;

public static class AppointmentMail
{
    public static string FormatWhen(DateTime scheduledLocal) =>
        scheduledLocal.ToString("dddd, d MMM yyyy, h:mm tt") + " IST";

    public static string DoctorBookingBody(string patientName, DateTime scheduledLocal, string approveUrl, string rejectUrl) =>
        $"""
        Dear Doctor,
        A new appointment has been booked.
        Patient: {patientName}
        Time: {FormatWhen(scheduledLocal)}
        Please approve or reject.

        Approve: {approveUrl}
        Reject: {rejectUrl}
        """;

    public static string DoctorRescheduleBody(string patientName, DateTime scheduledLocal) =>
        $"""
        Dear Doctor,
        The appointment has been rescheduled.
        Patient: {patientName}
        New Time: {FormatWhen(scheduledLocal)}
        """;

    public static string PatientBookingBody(string patientName, string doctorName, DateTime scheduledLocal, string clinicAddress) =>
        $"""
        Dear {patientName},
        Your appointment has been booked successfully.
        Doctor: {doctorName}
        Time: {FormatWhen(scheduledLocal)}
        Location: {clinicAddress}
        """;

    public static string Token(int appointmentId, string action, string secret)
    {
        var payload = $"{appointmentId}:{action.ToLowerInvariant()}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        return Convert.ToHexString(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload)));
    }

    public static bool TokenValid(int appointmentId, string action, string token, string secret)
    {
        if (string.IsNullOrWhiteSpace(token) || string.IsNullOrWhiteSpace(secret))
        {
            return false;
        }

        var expected = Token(appointmentId, action, secret);
        var actual = token.Trim().ToUpperInvariant();
        if (expected.Length != actual.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(actual));
    }

    public static string ToHtml(string body, string? approveUrl = null, string? rejectUrl = null)
    {
        var html = string.Join("", body.Split('\n').Select(line =>
            string.IsNullOrWhiteSpace(line) ? "<br/>" : $"<p style=\"margin:0 0 10px;font-family:Arial,sans-serif;font-size:15px;color:#0f172a\">{System.Net.WebUtility.HtmlEncode(line)}</p>"));

        if (!string.IsNullOrWhiteSpace(approveUrl) && !string.IsNullOrWhiteSpace(rejectUrl))
        {
            html += $"""
                <p style="margin:24px 0 0">
                  <a href="{approveUrl}" style="display:inline-block;padding:10px 18px;background:#0f766e;color:#fff;text-decoration:none;border-radius:10px;font-family:Arial,sans-serif;font-weight:600">Approve</a>
                  &nbsp;
                  <a href="{rejectUrl}" style="display:inline-block;padding:10px 18px;background:#e11d48;color:#fff;text-decoration:none;border-radius:10px;font-family:Arial,sans-serif;font-weight:600">Reject</a>
                </p>
                """;
        }

        return $"<html><body style=\"background:#f8fafc;padding:24px\"><div style=\"max-width:560px;margin:auto;background:#fff;border-radius:16px;padding:24px;border:1px solid #e2e8f0\">{html}<p style=\"margin-top:28px;color:#64748b;font-size:12px;font-family:Arial,sans-serif\">Anuj Clinic</p></div></body></html>";
    }

    public static string DisplayName(Patient patient) =>
        string.IsNullOrWhiteSpace(patient.Name) ? "Patient" : patient.Name.Trim();

    public static string DisplayDoctor(Doctor doctor) =>
        string.IsNullOrWhiteSpace(doctor.Name) ? "Doctor" : doctor.Name.Trim();
}
