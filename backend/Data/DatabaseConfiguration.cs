using Microsoft.EntityFrameworkCore;
using Npgsql;

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
        var builder = ToBuilder(connectionString);
        EnsureSessionPoolerUser(builder);
        builder.SslMode = SslMode.Require;
        if (builder.Port == 6543)
        {
            builder.Port = 5432;
        }

        return builder.ConnectionString;
    }

    public static (string Host, string Username, string Database) Describe(string connectionString)
    {
        var builder = ToBuilder(Normalize(connectionString));
        return (builder.Host ?? "unknown", builder.Username ?? "unknown", builder.Database ?? "unknown");
    }

    public static string HostName(string connectionString) => Describe(connectionString).Host;

    public static void UseSupabase(this DbContextOptionsBuilder options, string connectionString)
    {
        options.UseNpgsql(Normalize(connectionString), npgsql =>
        {
            npgsql.EnableRetryOnFailure(5);
            npgsql.CommandTimeout(60);
        });
    }

    private static NpgsqlConnectionStringBuilder ToBuilder(string connectionString)
    {
        var value = Sanitize(connectionString);
        if (value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            || value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            value = FromPostgresUri(value);
        }

        return new NpgsqlConnectionStringBuilder(value);
    }

    private static void EnsureSessionPoolerUser(NpgsqlConnectionStringBuilder builder)
    {
        var host = builder.Host ?? string.Empty;
        var user = builder.Username ?? string.Empty;
        var isPooler = host.Contains(".pooler.supabase.com", StringComparison.OrdinalIgnoreCase);
        var isDirect = host.StartsWith("db.", StringComparison.OrdinalIgnoreCase)
            && host.EndsWith(".supabase.co", StringComparison.OrdinalIgnoreCase);

        if (isDirect)
        {
            throw new InvalidOperationException(
                "db.*.supabase.co is IPv6-only and Render cannot reach it. Set ConnectionStrings__DefaultConnection to the Session pooler URI from Supabase → Database (host aws-0-REGION.pooler.supabase.com, username postgres.PROJECT_REF, port 5432).");
        }

        if (isPooler && user.Equals("postgres", StringComparison.OrdinalIgnoreCase))
        {
            var projectRef = FirstNonEmpty(
                Environment.GetEnvironmentVariable("SUPABASE_PROJECT_REF"),
                ProjectRefFromUrl(Environment.GetEnvironmentVariable("SUPABASE_URL")));
            if (string.IsNullOrWhiteSpace(projectRef))
            {
                throw new InvalidOperationException(
                    "Session pooler rejects user \"postgres\". The username must be postgres.YOUR_PROJECT_REF. Copy the Session pooler URI from the Supabase dashboard without changing the username, or set SUPABASE_PROJECT_REF.");
            }

            builder.Username = $"postgres.{projectRef}";
        }
    }

    private static string? ProjectRefFromUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        const string marker = "https://";
        if (!url.StartsWith(marker, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var host = url[marker.Length..].Split('/')[0];
        var suffix = ".supabase.co";
        return host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
            ? host[..^suffix.Length]
            : null;
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

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = host,
            Port = port,
            Database = database,
            Username = user,
            Password = password,
            SslMode = SslMode.Require
        };
        return builder.ConnectionString;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
}
