using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Data;

public static class DatabaseConfiguration
{
    public const string DummyDesignTime =
        "Host=127.0.0.1;Port=5432;Database=postgres;Username=postgres;Password=postgres;SSL Mode=Disable";

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
            return FromPostgresUri(value);
        }

        return EnsureSsl(value);
    }

    public static string HostName(string connectionString)
    {
        foreach (var part in Normalize(connectionString).Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var pair = part.Split('=', 2);
            if (pair.Length == 2 && pair[0].Equals("Host", StringComparison.OrdinalIgnoreCase))
            {
                return pair[1];
            }
        }

        return "unknown";
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
                "ConnectionStrings__DefaultConnection is missing user@host. Paste either the Supabase URI or Host=...;Username=...;Password=...;Port=5432;Database=postgres");
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
                "Could not read the database host. On Render, paste the Supabase Session pooler string as Host=db...;Port=5432;Database=postgres;Username=postgres.xxx;Password=...;SSL Mode=Require");
        }

        return $"Host={host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true";
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

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
