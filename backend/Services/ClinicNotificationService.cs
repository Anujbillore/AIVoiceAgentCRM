using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Hubs;
using AiVoicePortal.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Services;

public interface IClinicNotificationService
{
    Task<ClinicNotificationDto> PublishAsync(
        string title,
        string detail,
        string tag,
        string audience = "Staff",
        int? doctorId = null,
        string? relatedType = null,
        int? relatedId = null,
        CancellationToken cancellationToken = default);

    Task<List<ClinicNotificationDto>> ListAsync(ICurrentUserService current, CancellationToken cancellationToken = default);
    Task<int> UnreadCountAsync(ICurrentUserService current, CancellationToken cancellationToken = default);
    Task MarkReadAsync(int id, ICurrentUserService current, CancellationToken cancellationToken = default);
    Task MarkAllReadAsync(ICurrentUserService current, CancellationToken cancellationToken = default);
}

public class ClinicNotificationService : IClinicNotificationService
{
    private readonly AppDbContext _db;
    private readonly IHubContext<DashboardHub> _hub;

    public ClinicNotificationService(AppDbContext db, IHubContext<DashboardHub> hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<ClinicNotificationDto> PublishAsync(
        string title,
        string detail,
        string tag,
        string audience = "Staff",
        int? doctorId = null,
        string? relatedType = null,
        int? relatedId = null,
        CancellationToken cancellationToken = default)
    {
        var row = new ClinicNotification
        {
            Title = title.Trim(),
            Detail = detail.Trim(),
            Tag = string.IsNullOrWhiteSpace(tag) ? "General" : tag.Trim(),
            CreatedAt = DateTime.UtcNow,
            Audience = string.IsNullOrWhiteSpace(audience) ? "Staff" : audience.Trim(),
            DoctorId = doctorId,
            RelatedType = relatedType,
            RelatedId = relatedId
        };
        _db.ClinicNotifications.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        var dto = ToDto(row);
        await _hub.Clients.All.SendAsync("NotificationAdded", dto, cancellationToken);
        return dto;
    }

    public async Task<List<ClinicNotificationDto>> ListAsync(ICurrentUserService current, CancellationToken cancellationToken = default)
    {
        var query = await ScopeAsync(_db.ClinicNotifications.AsQueryable(), current, cancellationToken);
        var rows = await query
            .OrderByDescending(n => n.CreatedAt)
            .Take(100)
            .ToListAsync(cancellationToken);
        return rows.Select(ToDto).ToList();
    }

    public async Task<int> UnreadCountAsync(ICurrentUserService current, CancellationToken cancellationToken = default)
    {
        var query = await ScopeAsync(_db.ClinicNotifications.AsQueryable(), current, cancellationToken);
        return await query.CountAsync(n => !n.IsRead, cancellationToken);
    }

    public async Task MarkReadAsync(int id, ICurrentUserService current, CancellationToken cancellationToken = default)
    {
        var query = await ScopeAsync(_db.ClinicNotifications.AsQueryable(), current, cancellationToken);
        var row = await query.FirstOrDefaultAsync(n => n.Id == id, cancellationToken);
        if (row is null)
        {
            return;
        }

        row.IsRead = true;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllReadAsync(ICurrentUserService current, CancellationToken cancellationToken = default)
    {
        var query = await ScopeAsync(_db.ClinicNotifications.AsQueryable(), current, cancellationToken);
        var rows = await query.Where(n => !n.IsRead).ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.IsRead = true;
        }

        if (rows.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
    }

    private static async Task<IQueryable<ClinicNotification>> ScopeAsync(
        IQueryable<ClinicNotification> query,
        ICurrentUserService current,
        CancellationToken cancellationToken)
    {
        if (current.IsAdmin)
        {
            return query.Where(n => n.Audience == "Staff" || n.Audience == "Admin" || n.Audience == "All");
        }

        if (current.IsDoctor)
        {
            var doctorId = await current.GetDoctorIdAsync(cancellationToken);
            if (!doctorId.HasValue)
            {
                return query.Where(n => false);
            }

            // Clinic-wide staff notices (DoctorId null) + this doctor's own notices.
            return query.Where(n =>
                n.Audience == "All"
                || ((n.Audience == "Staff" || n.Audience == "Doctor")
                    && (n.DoctorId == null || n.DoctorId == doctorId)));
        }

        return query.Where(n => false);
    }

    private static ClinicNotificationDto ToDto(ClinicNotification row) =>
        new(row.Id, row.Title, row.Detail, row.Tag, row.CreatedAt, row.IsRead, row.RelatedType, row.RelatedId);
}
