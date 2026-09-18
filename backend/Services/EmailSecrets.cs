using System.Text.Json;
using System.Text.Json.Nodes;

namespace AiVoicePortal.Api.Services;

public static class EmailSecrets
{
    public static void SaveSmtpPassword(string contentRoot, IConfiguration config, string password)
    {
        var cleaned = password.Replace(" ", "", StringComparison.Ordinal).Trim();
        if (string.IsNullOrWhiteSpace(cleaned))
        {
            throw new InvalidOperationException("Enter the Gmail App Password for billoreanuj24@gmail.com.");
        }

        var path = Path.Combine(contentRoot, "appsettings.Local.json");
        JsonNode root = File.Exists(path)
            ? JsonNode.Parse(File.ReadAllText(path)) ?? new JsonObject()
            : new JsonObject();

        var email = root["Email"] as JsonObject ?? new JsonObject();
        email["SmtpHost"] = "smtp.gmail.com";
        email["SmtpPort"] = 587;
        email["EnableSsl"] = true;
        email["From"] = EmailService.CompanyFrom;
        email["Username"] = EmailService.CompanyFrom;
        email["Password"] = cleaned;
        email["OverrideRecipients"] = true;
        email["TestDoctorTo"] = EmailService.TestInbox;
        email["TestPatientTo"] = EmailService.TestInbox;
        root["Email"] = email;

        File.WriteAllText(path, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
        config["Email:Password"] = cleaned;
        config["Email:Username"] = EmailService.CompanyFrom;
        config["Email:From"] = EmailService.CompanyFrom;
        config["Email:SmtpHost"] = "smtp.gmail.com";
        config["Email:SmtpPort"] = "587";
        config["Email:EnableSsl"] = "true";
        config["Email:OverrideRecipients"] = "true";
        config["Email:TestDoctorTo"] = EmailService.TestInbox;
        config["Email:TestPatientTo"] = EmailService.TestInbox;
    }
}
