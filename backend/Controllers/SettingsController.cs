using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;
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

    public SettingsController(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    [HttpGet("system")]
    public async Task<ActionResult<SystemStatusDto>> System(CancellationToken cancellationToken)
    {
        var raw = DatabaseConfiguration.ReadRaw(_config) ?? string.Empty;
        var connected = await _db.Database.CanConnectAsync(cancellationToken);
        return new SystemStatusDto(
            "Supabase PostgreSQL",
            string.IsNullOrWhiteSpace(raw) ? "not configured" : DatabaseConfiguration.HostName(raw),
            connected,
            await _db.Patients.CountAsync(cancellationToken),
            await _db.Doctors.CountAsync(cancellationToken),
            await _db.Appointments.CountAsync(cancellationToken),
            await _db.CallLogs.CountAsync(cancellationToken),
            await _db.EmailMessages.CountAsync(cancellationToken));
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
