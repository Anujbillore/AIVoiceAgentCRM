namespace AiVoicePortal.Api.Services;

public static class AppointmentStatuses
{
    public static bool IsPending(string? status) =>
        Matches(status, "Scheduled", "Booked", "Pending", "Confirmed", "Not Attended");

    public static bool IsCompleted(string? status) =>
        Matches(status, "Completed", "Done", "Visited");

    public static bool IsCancelled(string? status) =>
        Matches(status, "Cancelled", "Canceled");

    public static bool IsBooked(string? status) =>
        !IsCancelled(status) && (IsPending(status) || IsCompleted(status));

    public static bool TryNormalize(string? status, out string normalized)
    {
        if (IsCompleted(status))
        {
            normalized = "Completed";
            return true;
        }

        if (IsCancelled(status))
        {
            normalized = "Cancelled";
            return true;
        }

        if (IsPending(status) || Matches(status, "No Show"))
        {
            normalized = "Not Attended";
            return true;
        }

        normalized = string.Empty;
        return false;
    }

    private static bool Matches(string? status, params string[] values) =>
        !string.IsNullOrWhiteSpace(status)
        && values.Any(value => value.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
}
