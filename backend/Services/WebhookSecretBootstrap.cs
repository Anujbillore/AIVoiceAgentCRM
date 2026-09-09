using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiVoicePortal.Api.Services;

public static class WebhookSecretBootstrap
{
    public static string Ensure(IConfiguration configuration, IHostEnvironment environment)
    {
        var existing = configuration["SarvamManaged:WebhookSecret"];
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        var generated = Guid.NewGuid().ToString("N");
        configuration["SarvamManaged:WebhookSecret"] = generated;

        var localPath = Path.Combine(environment.ContentRootPath, "appsettings.Local.json");
        try
        {
            var root = File.Exists(localPath)
                ? JsonNode.Parse(File.ReadAllText(localPath)) as JsonObject ?? []
                : [];
            var section = root["SarvamManaged"] as JsonObject ?? [];
            section["WebhookSecret"] = generated;
            root["SarvamManaged"] = section;
            File.WriteAllText(localPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception)
        {
            // Secret stays in memory for this process if Local.json cannot be written.
        }

        return generated;
    }
}
