using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Doctor}")]
public class NotificationsController : ControllerBase
{
    private readonly IClinicNotificationService _notifications;
    private readonly ICurrentUserService _current;

    public NotificationsController(IClinicNotificationService notifications, ICurrentUserService current)
    {
        _notifications = notifications;
        _current = current;
    }

    [HttpGet]
    public async Task<ActionResult<List<ClinicNotificationDto>>> List(CancellationToken cancellationToken)
    {
        return await _notifications.ListAsync(_current, cancellationToken);
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<object>> UnreadCount(CancellationToken cancellationToken)
    {
        var count = await _notifications.UnreadCountAsync(_current, cancellationToken);
        return new { count };
    }

    [HttpPost("{id:int}/read")]
    public async Task<IActionResult> MarkRead(int id, CancellationToken cancellationToken)
    {
        await _notifications.MarkReadAsync(id, _current, cancellationToken);
        return NoContent();
    }

    [HttpPost("read-all")]
    public async Task<IActionResult> MarkAllRead(CancellationToken cancellationToken)
    {
        await _notifications.MarkAllReadAsync(_current, cancellationToken);
        return NoContent();
    }

    [HttpGet("emails")]
    public ActionResult<object> Emails() => Ok(Array.Empty<object>());
}
