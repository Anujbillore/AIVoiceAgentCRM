using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;

    public DashboardController(AppDbContext db, ICurrentUserService current)
    {
        _db = db;
        _current = current;
    }

    [HttpGet]
    public async Task<ActionResult<DashboardStatsDto>> Get(
        [FromQuery] DateTime? from,
        [FromQuery] DateTime? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 8,
        CancellationToken cancellationToken = default)
    {
        var end = (to ?? DateTime.Today).Date;
        var start = (from ?? end.AddDays(-6)).Date;
        if (start > end)
        {
            (start, end) = (end, start);
        }

        if ((end - start).TotalDays > 89)
        {
            start = end.AddDays(-89);
        }

        var rangeEnd = end.AddDays(1);
        var doctorId = _current.IsDoctor ? await _current.GetDoctorIdAsync(cancellationToken) : null;
        var appointments = _db.Appointments.Where(a => a.ScheduledAt >= start && a.ScheduledAt < rangeEnd);
        if (doctorId.HasValue)
        {
            appointments = appointments.Where(a => a.DoctorId == doctorId);
        }

        var calls = _db.CallLogs.Where(c => c.Timestamp >= start && c.Timestamp < rangeEnd);
        var callsInRange = await calls.CountAsync(cancellationToken);
        var appointmentsInRange = await appointments.CountAsync(a => a.Status != "Cancelled", cancellationToken);
        var upcoming = await _db.Appointments.CountAsync(a =>
            (!doctorId.HasValue || a.DoctorId == doctorId) && a.ScheduledAt >= DateTime.Now && a.Status == "Scheduled", cancellationToken);
        var doctors = await _db.Doctors.CountAsync(d => d.IsActive, cancellationToken);
        var pending = await appointments.CountAsync(a => a.Status == "Scheduled", cancellationToken);
        var completed = await appointments.CountAsync(a => a.Status == "Completed", cancellationToken);
        var cancelled = await appointments.CountAsync(a => a.Status == "Cancelled", cancellationToken);
        var booked = pending + completed;

        var callVolume = new List<DailyCountDto>();
        var appointmentStats = new List<DailyCountDto>();
        var statusByDay = new List<AppointmentStatusDayDto>();
        var days = (int)(end - start).TotalDays;
        for (var i = 0; i <= days; i++)
        {
            var day = start.AddDays(i);
            var next = day.AddDays(1);
            var dayRows = appointments.Where(a => a.ScheduledAt >= day && a.ScheduledAt < next);
            var callCount = await calls.CountAsync(c => c.Timestamp >= day && c.Timestamp < next, cancellationToken);
            var pendingDay = await dayRows.CountAsync(a => a.Status == "Scheduled", cancellationToken);
            var completedDay = await dayRows.CountAsync(a => a.Status == "Completed", cancellationToken);
            var cancelledDay = await dayRows.CountAsync(a => a.Status == "Cancelled", cancellationToken);
            var bookedDay = pendingDay + completedDay;
            var label = days <= 13 ? day.ToString("ddd d MMM") : day.ToString("d MMM");
            callVolume.Add(new DailyCountDto(label, callCount));
            appointmentStats.Add(new DailyCountDto(label, bookedDay));
            statusByDay.Add(new AppointmentStatusDayDto(label, bookedDay, pendingDay, completedDay, cancelledDay));
        }

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 50 ? 8 : pageSize;
        var actionTotal = await calls.CountAsync(cancellationToken);
        var items = await calls
            .OrderByDescending(c => c.Timestamp)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(c => new CallLogDto(c.Id, c.CallerName, c.CallerPhone, c.Summary, c.ActionTaken, c.Intent, c.Transcript, c.Timestamp, c.Outcome, c.EscalationReason, c.TransferType, c.Confidence, c.Sentiment, c.ConsentGiven))
            .ToListAsync(cancellationToken);

        var escalated = await calls.CountAsync(c => c.Outcome == "Escalated", cancellationToken);
        var callbacks = await _db.CallCallbacks.CountAsync(c => c.CreatedAt >= start && c.CreatedAt < rangeEnd && c.Status == "Queued", cancellationToken);
        var containment = callsInRange == 0 ? 0 : Math.Round((callsInRange - escalated) * 100.0 / callsInRange, 1);

        return new DashboardStatsDto(
            start,
            end,
            callsInRange,
            appointmentsInRange,
            upcoming,
            doctors,
            booked,
            pending,
            completed,
            cancelled,
            callVolume,
            appointmentStats,
            statusByDay,
            items,
            actionTotal,
            page,
            pageSize,
            [],
            containment,
            escalated,
            callbacks);
    }
}
