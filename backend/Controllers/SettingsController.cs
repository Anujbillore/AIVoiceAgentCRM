using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/settings")]
[Authorize(Roles = AppRoles.Admin)]
public class SettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;
    private readonly IEmailService _email;
    private readonly IAppointmentService _appointments;

    public SettingsController(
        AppDbContext db,
        IConfiguration config,
        IWebHostEnvironment env,
        IEmailService email,
        IAppointmentService appointments)
    {
        _db = db;
        _config = config;
        _env = env;
        _email = email;
        _appointments = appointments;
    }

    [HttpGet("system")]
    public async Task<ActionResult<SystemStatusDto>> System(CancellationToken cancellationToken)
    {
        var raw = DatabaseConfiguration.ReadRaw(_config) ?? string.Empty;
        var connected = await _db.Database.CanConnectAsync(cancellationToken);
        return new SystemStatusDto(
            "Clinic database",
            connected ? "Connected" : "Disconnected",
            connected,
            await _db.Patients.CountAsync(cancellationToken),
            await _db.Doctors.CountAsync(cancellationToken),
            await _db.Appointments.CountAsync(cancellationToken),
            await _db.CallLogs.CountAsync(cancellationToken),
            await _db.EmailMessages.CountAsync(cancellationToken),
            _email.IsSmtpConfigured(),
            EmailService.CompanyFrom,
            EmailService.TestInbox);
    }

    [HttpGet("email")]
    public ActionResult<EmailSettingsDto> GetEmail() =>
        new EmailSettingsDto(_email.IsSmtpConfigured(), EmailService.CompanyFrom, EmailService.TestInbox);

    [HttpPut("email")]
    public async Task<ActionResult<EmailSettingsDto>> SaveEmail(SaveEmailSettingsRequest request, CancellationToken cancellationToken)
    {
        try
        {
            EmailSecrets.SaveSmtpPassword(_env.ContentRootPath, _config, request.Password);
            var latest = await _db.Appointments.OrderByDescending(a => a.Id).FirstOrDefaultAsync(cancellationToken);
            if (latest is not null)
            {
                await _appointments.ResendBookingEmailsAsync(latest.Id, cancellationToken);
            }

            return new EmailSettingsDto(true, EmailService.CompanyFrom, EmailService.TestInbox);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpPost("email/resend")]
    public async Task<IActionResult> ResendLastBooking(CancellationToken cancellationToken)
    {
        if (!_email.IsSmtpConfigured())
        {
            return BadRequest(new { message = "Gmail App Password is not configured yet." });
        }

        try
        {
            var latest = await _db.Appointments.OrderByDescending(a => a.Id).FirstOrDefaultAsync(cancellationToken);
            if (latest is null)
            {
                return BadRequest(new { message = "No appointment found to email." });
            }

            await _appointments.ResendBookingEmailsAsync(latest.Id, cancellationToken);
            return Ok(new { message = $"Booking emails resent for appointment {latest.Id}." });
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.InnerException?.Message ?? ex.Message });
        }
    }

    [HttpGet("ai")]
    public async Task<ActionResult<AiSettingsDto>> GetAi(CancellationToken cancellationToken)
    {
        var settings = await _db.AiSettings.FirstAsync(cancellationToken);
        return ToDto(settings);
    }

    [HttpPut("ai")]
    public async Task<ActionResult<AiSettingsDto>> UpdateAi(AiSettingsDto request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AgentName) || string.IsNullOrWhiteSpace(request.WelcomeMessage))
        {
            return BadRequest(new { message = "Agent name and welcome message are required." });
        }

        var settings = await _db.AiSettings.FirstAsync(cancellationToken);
        settings.AgentName = request.AgentName;
        settings.WelcomeMessage = request.WelcomeMessage;
        settings.Language = string.IsNullOrWhiteSpace(request.Language) ? "en-IN" : request.Language;
        settings.Instructions = request.Instructions;
        settings.VoiceSpeaker = request.VoiceSpeaker;
        settings.ConsentMessage = request.ConsentMessage;
        settings.TransferNumber = request.TransferNumber;
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(settings);
    }

    private static AiSettingsDto ToDto(AiSettings s) =>
        new(s.Id, s.AgentName, s.WelcomeMessage, s.Language, s.Instructions, s.VoiceSpeaker, s.ConsentMessage, s.TransferNumber);
}
