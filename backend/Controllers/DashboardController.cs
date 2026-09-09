using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Hubs;
using AiVoicePortal.Api.Models;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly ICurrentUserService _current;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ISarvamAiService _ai;

    public DashboardController(AppDbContext db, ICurrentUserService current, IHubContext<DashboardHub> hub, ISarvamAiService ai)
    {
        _db = db;
        _current = current;
        _hub = hub;
        _ai = ai;
    }

    [HttpGet]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Doctor}")]
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

        var startIst = IndiaTime.StartOfDayIst(start);
        var rangeEndIst = IndiaTime.StartOfDayIst(end.AddDays(1));
        var startUtc = IndiaTime.StartOfDayUtc(start);
        var rangeEndUtc = IndiaTime.StartOfDayUtc(end.AddDays(1));
        var doctorId = _current.IsDoctor ? await _current.GetDoctorIdAsync(cancellationToken) : null;

        var appointmentRows = await _db.Appointments
            .Where(a =>
                (!doctorId.HasValue || a.DoctorId == doctorId)
                && ((a.ScheduledAt >= startIst && a.ScheduledAt < rangeEndIst)
                    || (a.CreatedAt >= startUtc && a.CreatedAt < rangeEndUtc)))
            .ToListAsync(cancellationToken);

        var callRows = await _db.CallLogs
            .Where(c => c.Timestamp >= startUtc && c.Timestamp < rangeEndUtc)
            .OrderByDescending(c => c.Timestamp)
            .ToListAsync(cancellationToken);

        var callsInRange = callRows.Count;
        var pending = appointmentRows.Count(a => AppointmentStatuses.IsPending(a.Status));
        var completed = appointmentRows.Count(a => AppointmentStatuses.IsCompleted(a.Status));
        var cancelled = appointmentRows.Count(a => AppointmentStatuses.IsCancelled(a.Status));
        var booked = pending + completed;
        var appointmentsInRange = booked;
        var upcoming = await _db.Appointments.CountAsync(a =>
            (!doctorId.HasValue || a.DoctorId == doctorId)
            && a.ScheduledAt >= IndiaTime.Now
            && (a.Status == "Scheduled" || a.Status == "Booked" || a.Status == "Pending" || a.Status == "Confirmed"), cancellationToken);
        var doctors = await _db.Doctors.CountAsync(d => d.IsActive, cancellationToken);

        var callVolume = new List<DailyCountDto>();
        var appointmentStats = new List<DailyCountDto>();
        var statusByDay = new List<AppointmentStatusDayDto>();
        var days = (int)(end - start).TotalDays;
        for (var i = 0; i <= days; i++)
        {
            var day = start.AddDays(i);
            var next = day.AddDays(1);
            var dayAppointments = appointmentRows.Where(a => ChartDay(a, startIst, rangeEndIst) >= day && ChartDay(a, startIst, rangeEndIst) < next).ToList();
            var callCount = callRows.Count(c =>
            {
                var ist = IndiaTime.ToIstLocal(DateTime.SpecifyKind(c.Timestamp, DateTimeKind.Utc));
                return ist >= day && ist < next;
            });
            var pendingDay = dayAppointments.Count(a => AppointmentStatuses.IsPending(a.Status));
            var completedDay = dayAppointments.Count(a => AppointmentStatuses.IsCompleted(a.Status));
            var cancelledDay = dayAppointments.Count(a => AppointmentStatuses.IsCancelled(a.Status));
            var bookedDay = pendingDay + completedDay;
            var label = days <= 13 ? day.ToString("ddd d MMM") : day.ToString("d MMM");
            callVolume.Add(new DailyCountDto(label, callCount));
            appointmentStats.Add(new DailyCountDto(label, bookedDay));
            statusByDay.Add(new AppointmentStatusDayDto(label, bookedDay, pendingDay, completedDay, cancelledDay));
        }

        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 8 : pageSize;
        var actionTotal = callRows.Count;
        var patientIds = callRows.Where(c => c.PatientId.HasValue).Select(c => c.PatientId!.Value).Distinct().ToList();
        var bookedIds = callRows.Select(CallLogDetails.BookedAppointmentId).Where(id => id.HasValue).Select(id => id!.Value).ToList();
        var matchedAppointments = await _db.Appointments
            .Include(a => a.Doctor)
            .Include(a => a.Patient)
            .Where(a => a.Status != "Cancelled"
                && (a.CreatedAt >= startUtc.AddDays(-2) && a.CreatedAt < rangeEndUtc.AddDays(2)
                    || patientIds.Contains(a.PatientId)
                    || bookedIds.Contains(a.Id)
                    || a.Notes.Contains("Voicebot")
                    || a.Notes.Contains("Sarvam")))
            .OrderByDescending(a => a.CreatedAt)
            .ToListAsync(cancellationToken);
        var patients = await _db.Patients.ToListAsync(cancellationToken);
        var translated = false;
        foreach (var call in callRows)
        {
            translated |= await _ai.EnsureEnglishAsync(call, cancellationToken);
        }

        if (translated)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        var unused = matchedAppointments.ToList();
        var callbackRows = await _db.CallCallbacks
            .Where(c => c.CreatedAt >= startUtc.AddDays(-1) && c.CreatedAt < rangeEndUtc.AddDays(1))
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        var allItems = callRows.Select(call =>
        {
            var appointment = CallLogDetails.FindAppointment(call, unused);
            if (appointment is not null)
            {
                unused.Remove(appointment);
            }

            var dto = CallLogDetails.ToDto(call, appointment, CallLogDetails.FindCallback(call, callbackRows), patients);
            if (string.IsNullOrWhiteSpace(call.CallerPhone) && !string.IsNullOrWhiteSpace(dto.CallerPhone))
            {
                call.CallerPhone = dto.CallerPhone;
            }

            if ((string.IsNullOrWhiteSpace(call.CallerName) || call.CallerName == "Unknown") && dto.CallerName != "Unknown caller")
            {
                call.CallerName = dto.CallerName;
            }

            return dto;
        }).ToList();
        if (callRows.Any(c => _db.Entry(c).State == EntityState.Modified))
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        var items = allItems.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var forwarded = callRows.Count(CallLogDetails.IsForwarded);
        var callbacks = allItems.Count(x => x.NeedsPersonalContact);
        var containment = callsInRange == 0 ? 0 : Math.Round((callsInRange - forwarded) * 100.0 / callsInRange, 1);

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
            forwarded,
            callbacks);
    }

    [HttpPost("calls/{id:int}/callback")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Doctor}")]
    public async Task<ActionResult<CallLogDto>> QueueCallback(int id, CancellationToken cancellationToken)
    {
        var call = await _db.CallLogs.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (call is null)
        {
            return NotFound();
        }

        var existing = await _db.CallCallbacks
            .Where(c => c.Status == "Queued")
            .OrderByDescending(c => c.CreatedAt)
            .ToListAsync(cancellationToken);
        var callback = CallLogDetails.FindCallback(call, existing);
        if (callback is null)
        {
            callback = new CallCallback
            {
                CallerName = CallLogDetails.DisplayName(call.CallerName),
                CallerPhone = CallLogDetails.DisplayPhone(call.CallerPhone),
                Reason = "Staff requested personal contact",
                Summary = string.IsNullOrWhiteSpace(call.Summary) ? "Arrange a callback" : call.Summary,
                Status = "Queued",
                Priority = false,
                CreatedAt = DateTime.UtcNow
            };
            _db.CallCallbacks.Add(callback);
            call.Outcome = "Callback";
            call.ActionTaken = "Callback queued — needs personal contact";
            await _db.SaveChangesAsync(cancellationToken);
            await _hub.Clients.All.SendAsync(
                "CallbackQueued",
                new { callerName = callback.CallerName, callerPhone = callback.CallerPhone, summary = callback.Summary },
                cancellationToken);
        }

        return CallLogDetails.ToDto(call, null, callback);
    }

    [HttpPost("calls/{id:int}/callback/complete")]
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Doctor}")]
    public async Task<ActionResult<CallLogDto>> CompleteCallback(int id, CancellationToken cancellationToken)
    {
        var call = await _db.CallLogs.FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        if (call is null)
        {
            return NotFound();
        }

        var rows = await _db.CallCallbacks.OrderByDescending(c => c.CreatedAt).ToListAsync(cancellationToken);
        var callback = CallLogDetails.FindCallback(call, rows);
        if (callback is not null)
        {
            callback.Status = "Completed";
        }

        call.ActionTaken = "Personal contact completed";
        await _db.SaveChangesAsync(cancellationToken);
        return CallLogDetails.ToDto(call, null, callback);
    }

    private static DateTime ChartDay(Appointment appointment, DateTime startIst, DateTime rangeEndIst)
    {
        if (appointment.ScheduledAt >= startIst && appointment.ScheduledAt < rangeEndIst)
        {
            return appointment.ScheduledAt.Date;
        }

        return IndiaTime.ToIstLocal(DateTime.SpecifyKind(appointment.CreatedAt, DateTimeKind.Utc)).Date;
    }
}
