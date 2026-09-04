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
        var value = connectionString.Trim();
        if (!value.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !value.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return EnsureSsl(value);
        }

        var uri = new Uri(value);
        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : string.Empty;
        var database = uri.AbsolutePath.Trim('/');
        if (string.IsNullOrWhiteSpace(database))
        {
            database = "postgres";
        }

        var port = uri.IsDefaultPort ? 5432 : uri.Port;
        return $"Host={uri.Host};Port={port};Database={database};Username={user};Password={password};SSL Mode=Require;Trust Server Certificate=true";
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
