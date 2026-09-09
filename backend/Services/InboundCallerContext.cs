namespace AiVoicePortal.Api.Services;

public sealed class InboundCallerContext
{
    private readonly object _gate = new();
    private string? _phone;
    private string? _name;
    private DateTime _at;

    public void Remember(string? phone, string? name = null)
    {
        lock (_gate)
        {
            if (!string.IsNullOrWhiteSpace(phone))
            {
                _phone = phone.Trim();
                _at = DateTime.UtcNow;
            }

            if (!string.IsNullOrWhiteSpace(name)
                && !name.Equals("Unknown", StringComparison.OrdinalIgnoreCase)
                && !name.Equals("Unknown caller", StringComparison.OrdinalIgnoreCase))
            {
                _name = name.Trim();
                _at = DateTime.UtcNow;
            }
        }
    }

    public string? RecentPhone()
    {
        lock (_gate)
        {
            return DateTime.UtcNow - _at <= TimeSpan.FromMinutes(45) ? _phone : null;
        }
    }

    public string? RecentName()
    {
        lock (_gate)
        {
            return DateTime.UtcNow - _at <= TimeSpan.FromMinutes(45) ? _name : null;
        }
    }
}
