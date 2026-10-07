using System.Text.RegularExpressions;

namespace AiVoicePortal.Api.Services;

public static class CallerIdentity
{
    public const string WebTestPhoneDigits = "7621806924";
    public const string WebTestPhone = "+917621806924";

    public static bool IsMissingPhone(string? value)
    {
        var digits = Last10(value);
        if (digits.Length < 10)
        {
            return true;
        }

        var folded = (value ?? "").Trim().ToLowerInvariant();
        if (folded.Contains("identifier", StringComparison.Ordinal)
            || folded is "unknown" or "null" or "none" or "n/a" or "na" or "test" or "web")
        {
            return true;
        }

        return digits is "0000000000" or "1111111111" or "1234567890"
            || digits.EndsWith("8047283845", StringComparison.Ordinal);
    }

    public static string ResolvePhone(params string?[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (IsMissingPhone(candidate))
            {
                continue;
            }

            return NormalizePhone(candidate);
        }

        return WebTestPhone;
    }

    public static bool UsedWebTestFallback(string? resolved) =>
        Last10(resolved) == WebTestPhoneDigits;

    public static bool HasRealName(string? name)
    {
        var folded = FoldName(name);
        if (folded.Length < 2
            || folded is "unknown" or "unknown caller" or "caller" or "patient"
            || folded.Contains("identifier", StringComparison.Ordinal))
        {
            return false;
        }

        if (folded.Contains("callback", StringComparison.Ordinal)
            || folded.Contains("requested", StringComparison.Ordinal)
            || folded.Contains("asked for", StringComparison.Ordinal)
            || folded.Contains("call back", StringComparison.Ordinal))
        {
            return false;
        }

        var words = folded.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length is >= 1 and <= 4 && words.All(word => word.Length >= 2);
    }

    public static bool PhonesMatch(string? left, string? right)
    {
        var a = Last10(left);
        var b = Last10(right);
        return a.Length >= 10 && a == b;
    }

    public static bool SamePerson(string? nameA, string? phoneA, string? nameB, string? phoneB) =>
        PhonesMatch(phoneA, phoneB) && NamesMatch(nameA, nameB);

    public static bool TextMentionsName(string? text, string? name)
    {
        if (!HasRealName(name) || string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var spoken = FoldName(text);
        var folded = FoldName(name);
        if (spoken.Contains(folded, StringComparison.Ordinal))
        {
            return true;
        }

        var first = folded.Split(' ', 2)[0];
        return first.Length >= 3 && Regex.IsMatch(spoken, $@"\b{Regex.Escape(first)}\b", RegexOptions.IgnoreCase);
    }

    public static bool NamesMatch(string? left, string? right)
    {
        var a = FoldName(left);
        var b = FoldName(right);
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b))
        {
            return false;
        }

        if (a == b)
        {
            return true;
        }

        var firstA = a.Split(' ', 2)[0];
        var firstB = b.Split(' ', 2)[0];
        if (firstA != firstB || firstA.Length < 3)
        {
            return false;
        }

        return a.StartsWith(b, StringComparison.Ordinal) || b.StartsWith(a, StringComparison.Ordinal);
    }

    public static string DisplayPhone(string? value)
    {
        var digits = Last10(value);
        return digits.Length == 10 ? $"+91 {digits[..5]} {digits[5..]}" : (value ?? "").Trim();
    }

    public static string NormalizePhone(string? value)
    {
        var digits = Regex.Replace(value ?? "", @"\D", "");
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

        return string.IsNullOrWhiteSpace(value) ? WebTestPhone : value.Trim();
    }

    public static string Last10(string? value)
    {
        var digits = Regex.Replace(value ?? "", @"\D", "");
        return digits.Length >= 10 ? digits[^10..] : digits;
    }

    public static string FoldName(string? value)
    {
        var text = Regex.Replace((value ?? "").Trim().ToLowerInvariant(), @"\s+", " ");
        text = Regex.Replace(text, @"^(mr|mrs|ms|miss|dr|shri|smt)\.?\s+", "");
        return text.Trim();
    }
}
