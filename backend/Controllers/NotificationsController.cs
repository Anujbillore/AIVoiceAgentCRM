using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/notifications")]
[Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Doctor}")]
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _db;

    public NotificationsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("emails")]
    public async Task<ActionResult<List<EmailMessageDto>>> Emails(CancellationToken cancellationToken)
    {
        var items = await _db.EmailMessages
            .OrderByDescending(e => e.SentAt)
            .Take(50)
            .Select(e => new EmailMessageDto(e.Id, e.Recipient, e.Subject, e.Body, e.SentAt, e.Delivery))
            .ToListAsync(cancellationToken);
        return items;
    }
}
