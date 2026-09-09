namespace AiVoicePortal.Api.Services;

public static class IndiaTime
{
    public static TimeZoneInfo Zone { get; } = TimeZoneInfo.FindSystemTimeZoneById(
        OperatingSystem.IsWindows() ? "India Standard Time" : "Asia/Kolkata");

    public static DateTime Now => TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Zone);

    public static DateTime ToIstLocal(DateTime value)
    {
        if (value.Kind == DateTimeKind.Utc)
        {
            return TimeZoneInfo.ConvertTimeFromUtc(value, Zone);
        }

        return DateTime.SpecifyKind(value, DateTimeKind.Unspecified);
    }

    public static DateTime ToUtcFromIst(DateTime istLocal)
    {
        var unspecified = DateTime.SpecifyKind(istLocal, DateTimeKind.Unspecified);
        return TimeZoneInfo.ConvertTimeToUtc(unspecified, Zone);
    }

    public static DateTime StartOfDayIst(DateTime date) =>
        DateTime.SpecifyKind(date.Date, DateTimeKind.Unspecified);

    public static DateTime StartOfDayUtc(DateTime istDate) => ToUtcFromIst(istDate.Date);
}
