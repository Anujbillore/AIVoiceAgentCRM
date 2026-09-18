using System.Net;
using System.Net.Http.Headers;
using System.Net.Mail;
using System.Text;
using System.Text.Json;
using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.Models;

namespace AiVoicePortal.Api.Services;

public interface IEmailService
{
    Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default);
    Task SendDoctorAsync(string? fallbackTo, string subject, string body, CancellationToken cancellationToken = default, string? htmlBody = null, bool requireDelivery = false);
    Task SendPatientAsync(string? fallbackTo, string subject, string body, CancellationToken cancellationToken = default, string? htmlBody = null, bool requireDelivery = false);
    bool IsSmtpConfigured();
}

public interface IEmailSender
{
    Task SendEmailAsync(string email, string subject, string htmlMessage);
}

public class EmailService : IEmailService, IEmailSender
{
    public const string CompanyFrom = "billoreanuj24@gmail.com";
    public const string TestInbox = "anujbillore112@gmail.com";

    private readonly IConfiguration _config;
    private readonly ILogger<EmailService> _logger;
    private readonly AppDbContext _db;
    private readonly IHttpClientFactory _http;

    public EmailService(IConfiguration config, ILogger<EmailService> logger, AppDbContext db, IHttpClientFactory http)
    {
        _config = config;
        _logger = logger;
        _db = db;
        _http = http;
    }

    public async Task SendEmailAsync(string email, string subject, string htmlMessage) =>
        await SendAsync(email, subject, htmlMessage);

    public Task SendDoctorAsync(string? fallbackTo, string subject, string body, CancellationToken cancellationToken = default, string? htmlBody = null, bool requireDelivery = false) =>
        SendRoutedAsync("Doctor", fallbackTo, subject, body, htmlBody, requireDelivery, cancellationToken);

    public Task SendPatientAsync(string? fallbackTo, string subject, string body, CancellationToken cancellationToken = default, string? htmlBody = null, bool requireDelivery = false) =>
        SendRoutedAsync("Patient", fallbackTo, subject, body, htmlBody, requireDelivery, cancellationToken);

    public bool IsSmtpConfigured() =>
        !string.IsNullOrWhiteSpace(_config["Email:SendGridApiKey"])
        || !string.IsNullOrWhiteSpace(_config["Email:Password"]);

    public async Task SendAsync(string to, string subject, string body, CancellationToken cancellationToken = default) =>
        await SendInternalAsync(to, subject, body, null, false, cancellationToken);

    private async Task SendRoutedAsync(
        string kind,
        string? fallbackTo,
        string subject,
        string body,
        string? htmlBody,
        bool requireDelivery,
        CancellationToken cancellationToken)
    {
        var overrideOn = !bool.TryParse(_config["Email:OverrideRecipients"], out var flag) || flag;
        var testTo = kind == "Doctor"
            ? _config["Email:TestDoctorTo"]
            : _config["Email:TestPatientTo"];
        var to = overrideOn
            ? (string.IsNullOrWhiteSpace(testTo) ? TestInbox : testTo)
            : fallbackTo ?? "";
        if (string.IsNullOrWhiteSpace(to))
        {
            _logger.LogWarning("No {Kind} email recipient for {Subject}", kind, subject);
            return;
        }

        await SendInternalAsync(to, subject, body, htmlBody, requireDelivery, cancellationToken);
    }

    private async Task SendInternalAsync(string to, string subject, string body, string? htmlBody, bool requireDelivery, CancellationToken cancellationToken)
    {
        var from = CompanyFrom;
        var html = htmlBody ?? (body.Contains('<') ? body : null);
        var delivery = "Logged";

        try
        {
            var sendGridKey = _config["Email:SendGridApiKey"];
            var host = _config["Email:SmtpHost"];
            var password = _config["Email:Password"];
            if (!string.IsNullOrWhiteSpace(sendGridKey))
            {
                await SendWithSendGridAsync(sendGridKey, from, to, subject, body, html, cancellationToken);
                delivery = "SendGrid";
            }
            else if (!string.IsNullOrWhiteSpace(host) && !string.IsNullOrWhiteSpace(password))
            {
                await SendWithSmtpAsync(host, from, to, subject, body, html, cancellationToken);
                delivery = "Smtp";
            }
            else
            {
                _logger.LogInformation(
                    "SMTP/SendGrid credentials missing. Email to {To}\nSubject: {Subject}\n{Body}",
                    to, subject, body);
                if (requireDelivery)
                {
                    throw new InvalidOperationException(
                        "Gmail App Password is missing. Save it in Settings so mail can leave the clinic mailbox billoreanuj24@gmail.com.");
                }
            }
        }
        catch (Exception ex)
        {
            delivery = "Failed";
            _logger.LogError(ex, "Could not send email to {To} ({Subject})", to, subject);
            _db.EmailMessages.Add(new EmailMessage
            {
                Recipient = to,
                Subject = subject,
                Body = body,
                SentAt = DateTime.UtcNow,
                Delivery = delivery
            });
            await _db.SaveChangesAsync(cancellationToken);
            if (requireDelivery)
            {
                throw;
            }

            return;
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

    private async Task SendWithSmtpAsync(
        string host,
        string from,
        string to,
        string subject,
        string body,
        string? html,
        CancellationToken cancellationToken)
    {
        var port = int.TryParse(_config["Email:SmtpPort"], out var p) ? p : 587;
        var username = string.IsNullOrWhiteSpace(_config["Email:Username"])
            ? CompanyFrom
            : _config["Email:Username"]!;
        var password = _config["Email:Password"];
        var enableSsl = !bool.TryParse(_config["Email:EnableSsl"], out var ssl) || ssl;

        using var client = new SmtpClient(host, port)
        {
            EnableSsl = enableSsl,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            UseDefaultCredentials = false,
            Credentials = new NetworkCredential(username, password)
        };

        using var message = new MailMessage(from, to, subject, html ?? body)
        {
            IsBodyHtml = !string.IsNullOrWhiteSpace(html)
        };
        await client.SendMailAsync(message, cancellationToken);
    }

    private async Task SendWithSendGridAsync(
        string apiKey,
        string from,
        string to,
        string subject,
        string body,
        string? html,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            personalizations = new[] { new { to = new[] { new { email = to } } } },
            from = new { email = from, name = "Anuj Clinic" },
            subject,
            content = new object[]
            {
                new { type = "text/plain", value = body },
                new { type = "text/html", value = html ?? $"<pre>{WebUtility.HtmlEncode(body)}</pre>" }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.sendgrid.com/v3/mail/send");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
        var client = _http.CreateClient(nameof(EmailService));
        using var response = await client.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            var detail = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException($"SendGrid {((int)response.StatusCode)}: {detail}");
        }
    }
}
