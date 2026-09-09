namespace AiVoicePortal.Api.Services;

public static class AppointmentStatuses
{
    public static bool IsPending(string? status) =>
        Matches(status, "Scheduled", "Booked", "Pending", "Confirmed");

    public static bool IsCompleted(string? status) =>
        Matches(status, "Completed", "Done", "Visited");

    public static bool IsCancelled(string? status) =>
        Matches(status, "Cancelled", "Canceled");

    public static bool IsBooked(string? status) =>
        !IsCancelled(status) && (IsPending(status) || IsCompleted(status));

    private static bool Matches(string? status, params string[] values) =>
        !string.IsNullOrWhiteSpace(status)
        && values.Any(value => value.Equals(status.Trim(), StringComparison.OrdinalIgnoreCase));
}
