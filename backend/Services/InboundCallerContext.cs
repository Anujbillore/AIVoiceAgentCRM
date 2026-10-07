using System.Collections.Generic;

namespace AiVoicePortal.Api.Services;

public sealed class InboundCallerContext
{
    private readonly object _gate = new();
    private readonly Dictionary<string, (string? Name, DateTime At)> _byPhone = new(StringComparer.Ordinal);

    public void Remember(string? phone, string? name = null)
    {
        var key = CallerIdentity.Last10(phone);
        if (key.Length < 10)
        {
            return;
        }

        lock (_gate)
        {
            _byPhone.TryGetValue(key, out var existing);
            var keepName = CallerIdentity.HasRealName(name) ? name!.Trim() : existing.Name;
            _byPhone[key] = (keepName, DateTime.UtcNow);
        }
    }

    public string? RecentNameFor(string? phone)
    {
        var key = CallerIdentity.Last10(phone);
        if (key.Length < 10)
        {
            return null;
        }

        lock (_gate)
        {
            if (!_byPhone.TryGetValue(key, out var row) || DateTime.UtcNow - row.At > TimeSpan.FromMinutes(45))
            {
                return null;
            }

            return CallerIdentity.HasRealName(row.Name) ? row.Name : null;
        }
    }

    public string? RecentPhone()
    {
        return null;
    }

    public string? RecentName()
    {
        return null;
    }
}
