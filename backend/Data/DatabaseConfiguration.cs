using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Data;

public static class DatabaseConfiguration
{
    public const string DummyDesignTime =
        "Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=postgres;SSL Mode=Disable";

    private static readonly Regex DirectSupabaseHost =
        new(@"^db\.([a-z0-9]+)\.supabase\.co$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private static readonly Regex SupabaseProjectUrl =
        new(@"https://([a-z0-9]+)\.supabase\.co", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string? ReadRaw(IConfiguration config) =>
        FirstNonEmpty(
            config["SUPABASE_DB_URL"],
            config["ConnectionStrings:Supabase"],
            config.GetConnectionString("DefaultConnection"));

    public static string Normalize(string connectionString)
    {
        var value = Sanitize(connectionString);
        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            value = FromPostgresUri(value);
        }
        else
        {
            value = EnsureSsl(value);
        }

        return PreferIpv4(PreferSessionPooler(value));
    }

    public static string HostName(string connectionString)
    {
        return ReadPair(Normalize(connectionString), "Host") ?? "unknown";
    }

    public static void UseSupabase(this DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(Normalize(connectionString), npgsql =>
        {
            npgsql.EnableRetryOnFailure(5);
            npgsql.CommandTimeout(60);
        });
    }

    private static string Sanitize(string connectionString)
    {
        var value = connectionString.Trim().Trim('"').Trim('\'');
        foreach (var prefix in new[] { "ConnectionStrings__DefaultConnection=", "DefaultConnection=", "DATABASE_URL=" })
        {
            if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                value = value[prefix.Length..].Trim().Trim('"').Trim('\'');
            }
        }

        return value.Replace("\r", string.Empty).Replace("\n", string.Empty).Trim();
    }

    private static string FromPostgresUri(string value)
    {
        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal);
        var rest = schemeEnd >= 0 ? value[(schemeEnd + 3)..] : value;
        var query = rest.IndexOf('?', StringComparison.Ordinal);
        if (query >= 0)
        {
            rest = rest[..query];
        }

        var at = rest.LastIndexOf('@');
        if (at <= 0)
        {
            throw new InvalidOperationException(
                "ConnectionStrings__DefaultConnection is missing user@host. In Supabase → Database, copy the Session pooler URI (port 5432) and leave the username as postgres.YOUR_PROJECT_REF.");
        }

        var userInfo = rest[..at];
        var hostAndDb = rest[(at + 1)..];
        var colon = userInfo.IndexOf(':');
        var user = Uri.UnescapeDataString(colon < 0 ? userInfo : userInfo[..colon]);
        var password = colon < 0 ? string.Empty : Uri.UnescapeDataString(userInfo[(colon + 1)..]);

        var slash = hostAndDb.IndexOf('/');
        var hostPort = slash < 0 ? hostAndDb : hostAndDb[..slash];
        var database = slash < 0 || slash == hostAndDb.Length - 1 ? "postgres" : hostAndDb[(slash + 1)..];

        var port = 5432;
        var portSep = hostPort.LastIndexOf(':');
        string host;
        if (portSep > 0 && int.TryParse(hostPort[(portSep + 1)..], out var parsedPort))
        {
            host = hostPort[..portSep];
            port = parsedPort;
        }
        else
        {
            host = hostPort;
        }

        if (string.IsNullOrWhiteSpace(host) || host.Contains(' '))
        {
            throw new InvalidOperationException(
                "Could not read the database host. Use the Supabase Session pooler (aws-0-….pooler.supabase.com), not db.*.supabase.co.");
        }

        return $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true";
    }

    private static string PreferSessionPooler(string npgsql)
    {
        var parts = ParsePairs(npgsql);
        if (!parts.TryGetValue("Host", out var host))
        {
            return npgsql;
        }

        var projectRef = ResolveProjectRef(parts, host);
        var direct = DirectSupabaseHost.Match(host);
        var isPooler = host.Contains(".pooler.supabase.com", StringComparison.OrdinalIgnoreCase);

        if (direct.Success)
        {
            var pooler = Environment.GetEnvironmentVariable("SUPABASE_POOLER_HOST");
            if (string.IsNullOrWhiteSpace(pooler))
            {
                throw new InvalidOperationException(
                    "db.*.supabase.co is IPv6-only and Render cannot reach it. Set ConnectionStrings__DefaultConnection to the Session pooler URI from Supabase → Database (host aws-0-REGION.pooler.supabase.com, username postgres.PROJECT_REF, port 5432).");
            }

            parts["Host"] = pooler.Trim();
            isPooler = true;
            projectRef ??= direct.Groups[1].Value;
        }

        if (!isPooler)
        {
            return JoinPairs(parts);
        }

        if (parts.TryGetValue("Port", out var port) && port == "6543")
        {
            parts["Port"] = "5432";
        }

        if (parts.TryGetValue("Username", out var user)
            && user.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(projectRef))
            {
                throw new InvalidOperationException(
                    "Session pooler rejects user \"postgres\". Copy the dashboard Session pooler URI unchanged so the username is postgres.YOUR_PROJECT_REF, or set SUPABASE_PROJECT_REF to your project Reference ID.");
            }

            parts["Username"] = $"postgres.{projectRef}";
        }

        return JoinPairs(parts);
    }

    private static string? ResolveProjectRef(Dictionary<string, string> parts, string host)
    {
        var direct = DirectSupabaseHost.Match(host);
        if (direct.Success)
        {
            return direct.Groups[1].Value;
        }

        if (parts.TryGetValue("Username", out var user)
            && user.StartsWith("postgres.", StringComparison.OrdinalIgnoreCase)
            && user.Length > "postgres.".Length)
        {
            return user["postgres.".Length..];
        }

        return FirstNonEmpty(
            Environment.GetEnvironmentVariable("SUPABASE_PROJECT_REF"),
            ProjectRefFromUrl(Environment.GetEnvironmentVariable("SUPABASE_URL")));
    }

    private static string? ProjectRefFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        var match = SupabaseProjectUrl.Match(url);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string PreferIpv4(string npgsql)
    {
        var parts = ParsePairs(npgsql);
        if (!parts.TryGetValue("Host", out var host) || IPAddress.TryParse(host, out _))
        {
            return npgsql;
        }

        try
        {
            var addresses = Dns.GetHostAddresses(host);
            var ipv4 = addresses.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
            if (ipv4 is null)
            {
                return npgsql;
            }

            parts["Host"] = ipv4.ToString();
            return JoinPairs(parts);
        }
        catch (SocketException)
        {
            return npgsql;
        }
    }

    private static string EnsureSsl(string value)
    {
        if (value.Contains("SSL Mode", StringComparison.OrdinalIgnoreCase)
            || value.Contains("SslMode", StringComparison.OrdinalIgnoreCase))
        {
            return value;
        }

        return value.TrimEnd(';') + ";SSL Mode=Require;Trust Server Certificate=true";
    }

    private static Dictionary<string, string> ParsePairs(string npgsql)
    {
        var parts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var segment in npgsql.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = segment.Split('=', 2);
            if (pair.Length == 2)
            {
                parts[pair[0]] = pair[1];
            }
        }

        return parts;
    }

    private static string JoinPairs(Dictionary<string, string> parts) =>
        string.Join(';', parts.Select(p => $"{p.Key}={p.Value}"));

    private static string? ReadPair(string npgsql, string key)
    {
        return ParsePairs(npgsql).TryGetValue(key, out var value) ? value : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
