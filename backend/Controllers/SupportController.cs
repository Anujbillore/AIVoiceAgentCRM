using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/support")]
[Authorize]
public class SupportController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IClinicNotificationService _notifications;

    public SupportController(AppDbContext db, IClinicNotificationService notifications)
    {
        _db = db;
        _notifications = notifications;
    }

    [HttpGet("tickets")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<List<CallLogDto>>> Tickets(CancellationToken cancellationToken)
    {
        var calls = await _db.CallLogs
            .Where(c =>
                c.Intent == "Support"
                || c.ActionTaken.Contains("Patient portal support")
                || c.ActionTaken.Contains("support request"))
            .OrderByDescending(c => c.Timestamp)
            .Take(100)
            .ToListAsync(cancellationToken);
        var callbacks = await _db.CallCallbacks
            .OrderByDescending(c => c.CreatedAt)
            .Take(200)
            .ToListAsync(cancellationToken);
        return calls.Select(c => CallLogDetails.ToDto(c, null, CallLogDetails.FindCallback(c, callbacks))).ToList();
    }

    [HttpPost("tickets/{id:int}/resolve")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<CallLogDto>> Resolve(int id, [FromBody] SupportReplyRequest? body, CancellationToken cancellationToken)
    {
        var call = await _db.CallLogs.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (call is null)
        {
            return NotFound();
        }

        var isSupport = call.Intent == "Support"
            || call.ActionTaken.Contains("Patient portal support", StringComparison.OrdinalIgnoreCase)
            || call.ActionTaken.Contains("support request", StringComparison.OrdinalIgnoreCase)
            || call.ActionTaken.StartsWith("Support reply:", StringComparison.OrdinalIgnoreCase)
            || call.ActionTaken.Equals("Support ticket resolved", StringComparison.OrdinalIgnoreCase);
        if (!isSupport)
        {
            return BadRequest(new { message = "That call is not a support ticket." });
        }

        var rows = await _db.CallCallbacks.OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        var callback = CallLogDetails.FindCallback(call, rows);
        if (callback is not null)
        {
            callback.Status = "Completed";
        }

        var reply = body?.Reply?.Trim() ?? "";
        call.ActionTaken = string.IsNullOrWhiteSpace(reply)
            ? "Support ticket resolved"
            : $"Support reply: {reply}";
        call.Outcome = "Completed";
        await _db.SaveChangesAsync(cancellationToken);

        await _notifications.PublishAsync(
            "Support ticket resolved",
            $"{call.CallerName}: {(string.IsNullOrWhiteSpace(reply) ? call.Summary : reply)}",
            "Support",
            "Staff",
            relatedType: "Support",
            relatedId: call.Id,
            cancellationToken: cancellationToken);

        return CallLogDetails.ToDto(call, null, callback);
    }
}

public record SupportReplyRequest(string? Reply);
