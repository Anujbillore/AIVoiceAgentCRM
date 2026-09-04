using System.Net;
using System.Net.Mail;
using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.Models;

namespace AiVoicePortal.Api.Services;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
}

public interface IEmailSender
{
    Task SendEmailAsync(string email, string subject, string htmlMessage);
}

public class EmailService : IEmailService, IEmailSender
{
    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly AppDbContext _db;

    public EmailService(IConfiguration config, ILogger<EmailService> logger, AppDbContext db)
    {
        _config = config;
        _logger = logger;
        _db = db;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage) =>
        await SendAsync(email, subject, htmlMessage);

    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default)
    {
        var host = _config["Email:SmtpHost"];
        var from = _config["Email:From"] ?? "anuj-ai@clinic.local";
        var delivery = "Logged";

        if (string.IsNullOrWhiteSpace(host))
        {
            _logger.LogInformation("SMTP not configured. Email to {To}\nSubject: {Subject}\n{Body}", to, subject, body);
        }
        else
        {
            var port = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
            var username = _config["Email:Username"];
            var password = _config["Email:Password"];
            var enableSsl = !bool.TryParse(_config["Email:EnableSsl"], out var ssl) || ssl;

            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                Credentials = string.IsNullOrWhiteSpace(username)
                    ? CredentialCache.DefaultNetworkCredentials
                    : new NetworkCredential(username, password)
            };

            var message = new MailMessage(from, to, subject, body) { IsBodyHtml = false };
            await client.SendMailAsync(message, cancellationToken);
            delivery = "Smtp";
        }

        _db.EmailMessages.Add(new EmailMessage
        {
            Recipient = to,
            Subject = subject,
            Body = body,
            SentAt = DateTime.UtcNow,
            Delivery = delivery
        });
        await _db.SaveChangesAsync(cancellationToken);
    }
}
