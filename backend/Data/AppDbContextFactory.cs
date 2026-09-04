using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace AiVoicePortal.Api.Data;

public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

        var config = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddJsonFile("appsettings.Local.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var raw = DatabaseConfiguration.ReadRaw(config);
        var connection = string.IsNullOrWhiteSpace(raw) || raw.Contains("ai-voice-portal.db", StringComparison.OrdinalIgnoreCase)
            ? DatabaseConfiguration.DummyDesignTime
            : raw;

        var options = new DbContextOptionsBuilder<AppDbContext>();
        options.UseSupabase(connection);
        return new AppDbContext(options.Options);
    }
}
