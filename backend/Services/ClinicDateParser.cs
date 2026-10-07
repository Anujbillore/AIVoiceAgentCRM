using System.Globalization;
using System.Text.RegularExpressions;

namespace AiVoicePortal.Api.Services;

public static class ClinicDateParser
{
    public static bool TryResolve(string? raw, out DateOnly date, out string? preferredTime)
    {
        date = DateOnly.FromDateTime(IndiaTime.Now);
        preferredTime = null;
        var text = (raw ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(text)
            || text is "earliest" or "soon" or "asap" or "any" or "anytime" or "any day" or "next available" or "first available")
        {
            return true;
        }

        preferredTime = ExtractPreferredTime(text);

        if (ContainsAny(text, "today", "aaj"))
        {
            date = DateOnly.FromDateTime(IndiaTime.Now);
            return true;
        }

        if (ContainsAny(text, "day after tomorrow", "parso", "parson"))
        {
            date = DateOnly.FromDateTime(IndiaTime.Now).AddDays(2);
            return true;
        }

        if (ContainsAny(text, "tomorrow", "kal"))
        {
            date = DateOnly.FromDateTime(IndiaTime.Now).AddDays(1);
            return true;
        }

        if (TryWeekday(text, out date))
        {
            return true;
        }

        var formats = new[]
        {
            "yyyy-MM-dd", "dd-MM-yyyy", "dd/MM/yyyy", "d/M/yyyy", "dd MMM yyyy", "d MMMM yyyy",
            "d MMM", "d MMMM", "MMM d", "MMMM d", "dd-MM", "dd/MM"
        };
        if (DateOnly.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out date)
            || DateOnly.TryParseExact(text, formats, CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.None, out date)
            || DateOnly.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out date)
            || DateTime.TryParse(text, CultureInfo.GetCultureInfo("en-IN"), DateTimeStyles.AllowWhiteSpaces, out var dt)
                && (date = DateOnly.FromDateTime(dt)) != default)
        {
            var now = DateOnly.FromDateTime(IndiaTime.Now);
            if (date.Year < now.Year)
            {
                date = new DateOnly(now.Year, date.Month, date.Day);
            }

            if (date < now)
            {
                date = date.AddYears(1);
            }

            return true;
        }

        var ordinal = Regex.Match(text, @"\b(\d{1,2})(st|nd|rd|th)?\b");
        if (ordinal.Success && int.TryParse(ordinal.Groups[1].Value, out var day) && day is >= 1 and <= 31)
        {
            var now = IndiaTime.Now;
            var candidate = new DateOnly(now.Year, now.Month, Math.Min(day, DateTime.DaysInMonth(now.Year, now.Month)));
            if (candidate < DateOnly.FromDateTime(now))
            {
                var nextMonth = now.AddMonths(1);
                candidate = new DateOnly(nextMonth.Year, nextMonth.Month, Math.Min(day, DateTime.DaysInMonth(nextMonth.Year, nextMonth.Month)));
            }

            date = candidate;
            return true;
        }

        return false;
    }

    public readonly record struct TimeWindow(TimeSpan? From, TimeSpan? To, string Label)
    {
        public static TimeWindow AllDay => new(null, null, "any time");
        public bool Restricts => From is not null || To is not null;

        public bool Matches(TimeSpan slot)
        {
            if (From is TimeSpan from && slot < from)
            {
                return false;
            }

            if (To is TimeSpan to && slot >= to)
            {
                return false;
            }

            return true;
        }
    }

    public static bool IsPlaceholderTime(string? value)
    {
        var text = (value ?? "").Trim();
        return text is "" or "09:00" or "9:00" or "09:00:00" or "9:00:00";
    }

    public static string CombineTimeHints(params string?[] parts)
    {
        var kept = parts
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part!.Trim())
            .Where(part => !IsPlaceholderTime(part));
        return string.Join(" ", kept);
    }

    public static bool TryParseSlotLocal(string? localStart, out DateTime time)
    {
        time = default;
        if (string.IsNullOrWhiteSpace(localStart))
        {
            return false;
        }

        var formats = new[]
        {
            "yyyy-MM-dd'T'HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ss.fff",
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd'T'HH:mm:ssK"
        };
        return DateTime.TryParseExact(localStart, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out time)
            || DateTime.TryParse(localStart, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out time);
    }

    public static TimeWindow ParseTimeWindow(params string?[] parts)
    {
        var text = CombineTimeHints(parts).ToLowerInvariant();
        text = Regex.Replace(text, @"\d{4}-\d{2}-\d{2}", " ");
        if (string.IsNullOrWhiteSpace(text) || IsPlaceholderTime(text.Trim()))
        {
            return TimeWindow.AllDay;
        }

        if (ContainsAny(text,
                "evening", "shaam", "sham", "shyam", "sanjh", "saanjh",
                "शाम", "सायंकाल", "संध्या",
                "night", "raat", "raath", "रात",
                "after 4", "after four", "after 4pm", "after 4 pm", "4 ke baad", "char baje", "char ke baad"))
        {
            return new TimeWindow(TimeSpan.FromHours(16), null, "evening");
        }

        if (ContainsAny(text,
                "afternoon", "after noon", "after 12", "after twelve", "after 12pm", "after 12 pm",
                "dupher", "duphar", "dopher", "dopahar", "dopehar", "doopher", "dupahar",
                "dophar", "dupehr", "dupehri",
                "बारह", "दोपहर", "दुपहर", "बारह बजे", "बारह के बाद",
                "12 ke baad", "barah ke baad", "barah baje",
                "post lunch", "lunch ke baad", "after lunch", "midday", "noon"))
        {
            return new TimeWindow(TimeSpan.FromHours(12), TimeSpan.FromHours(16), "afternoon");
        }

        if (ContainsAny(text,
                "morning", "subah", "subha", "suba", "savar", "savere", "saverey",
                "सुबह", "सवेरे", "सवेरा", "forenoon", "breakfast"))
        {
            return new TimeWindow(TimeSpan.FromHours(9), TimeSpan.FromHours(12), "morning");
        }

        var after = ContainsAny(text, "after", "baad", "from", "onwards", "onward");
        var before = ContainsAny(text, "before", "pehle", "until");
        foreach (Match match in Regex.Matches(text, @"\b(\d{1,2})(?::(\d{2}))(?::\d{2})?\s*(a\.?m\.?|p\.?m\.?)?\b|\b(\d{1,2})\s*(a\.?m\.?|p\.?m\.?)\b|\b(\d{1,2})\b"))
        {
            var hourText = FirstGroup(match, 1, 4, 6);
            var minuteText = match.Groups[2].Success ? match.Groups[2].Value : "0";
            var meridian = FirstGroup(match, 3, 5).Replace(".", "", StringComparison.Ordinal);
            if (!int.TryParse(hourText, CultureInfo.InvariantCulture, out var hour)
                || !int.TryParse(minuteText, CultureInfo.InvariantCulture, out var minute))
            {
                continue;
            }

            if (IsPlaceholderTime($"{hour:00}:{minute:00}") && string.IsNullOrWhiteSpace(meridian) && !after && !before)
            {
                continue;
            }

            if (hour == 12 && string.IsNullOrWhiteSpace(meridian) && !before)
            {
                meridian = "pm";
            }

            if (after && hour is >= 1 and <= 7 && string.IsNullOrWhiteSpace(meridian))
            {
                meridian = "pm";
            }

            if (meridian.StartsWith("p", StringComparison.Ordinal) && hour < 12)
            {
                hour += 12;
            }

            if (meridian.StartsWith("a", StringComparison.Ordinal) && hour == 12)
            {
                hour = 0;
            }

            if (hour is < 0 or > 23 || minute is < 0 or > 59)
            {
                continue;
            }

            var time = new TimeSpan(hour, minute, 0);
            var clock = $"{(hour % 12 == 0 ? 12 : hour % 12)}:{minute:00} {(hour >= 12 ? "PM" : "AM")}";
            if (after)
            {
                return new TimeWindow(time, null, $"after {clock}");
            }

            if (before)
            {
                return new TimeWindow(null, time, $"before {clock}");
            }

            return new TimeWindow(time, null, $"from {clock}");
        }

        return TimeWindow.AllDay;
    }

    private static string FirstGroup(Match match, params int[] indexes)
    {
        foreach (var index in indexes)
        {
            if (index <= match.Groups.Count && match.Groups[index].Success && !string.IsNullOrWhiteSpace(match.Groups[index].Value))
            {
                return match.Groups[index].Value;
            }
        }

        return "";
    }

    public static string? ExtractPreferredTime(string? raw)
    {
        var window = ParseTimeWindow(raw);
        if (window.From is TimeSpan from)
        {
            return $"{from.Hours:00}:{from.Minutes:00}";
        }

        return null;
    }

    private static bool TryWeekday(string text, out DateOnly date)
    {
        date = default;
        var map = new Dictionary<string, DayOfWeek>(StringComparer.OrdinalIgnoreCase)
        {
            ["sunday"] = DayOfWeek.Sunday,
            ["ravivar"] = DayOfWeek.Sunday,
            ["monday"] = DayOfWeek.Monday,
            ["somvar"] = DayOfWeek.Monday,
            ["somwar"] = DayOfWeek.Monday,
            ["tuesday"] = DayOfWeek.Tuesday,
            ["mangalvar"] = DayOfWeek.Tuesday,
            ["mangalwar"] = DayOfWeek.Tuesday,
            ["wednesday"] = DayOfWeek.Wednesday,
            ["budhvar"] = DayOfWeek.Wednesday,
            ["budhwar"] = DayOfWeek.Wednesday,
            ["thursday"] = DayOfWeek.Thursday,
            ["guruvar"] = DayOfWeek.Thursday,
            ["guruwar"] = DayOfWeek.Thursday,
            ["friday"] = DayOfWeek.Friday,
            ["shukravar"] = DayOfWeek.Friday,
            ["shukrawar"] = DayOfWeek.Friday,
            ["saturday"] = DayOfWeek.Saturday,
            ["shanivar"] = DayOfWeek.Saturday,
            ["shaniwar"] = DayOfWeek.Saturday
        };

        foreach (var pair in map)
        {
            if (!text.Contains(pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var today = DateOnly.FromDateTime(IndiaTime.Now);
            var delta = ((int)pair.Value - (int)today.DayOfWeek + 7) % 7;
            date = today.AddDays(delta);
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] tokens) =>
        tokens.Any(token =>
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                return false;
            }

            if (token.Any(ch => ch > 127))
            {
                return text.Contains(token, StringComparison.OrdinalIgnoreCase);
            }

            return Regex.IsMatch(text, $@"\b{Regex.Escape(token)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        });
}
