using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Hubs;
using AiVoicePortal.Api.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Services;

public interface IAppointmentService
{
    Task<AppointmentDto> BookAsync(BookAppointmentRequest request, bool pushDashboard = true, CancellationToken cancellationToken = default);
    Task<List<AppointmentDto>> ListAsync(int? doctorId, int? patientId, CancellationToken cancellationToken = default);
    Task<AppointmentPageDto> ListPagedAsync(int? doctorId, int? patientId, int page, int pageSize, CancellationToken cancellationToken = default);
    Task<AppointmentDto> UpdateStatusAsync(int id, string status, CancellationToken cancellationToken = default);
    Task<AppointmentDto> RescheduleAsync(int id, DateTime scheduledAt, CancellationToken cancellationToken = default);
}

public class AppointmentService : IAppointmentService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly IHubContext<DashboardHub> _hub;

    public AppointmentService(AppDbContext db, IEmailService email, IHubContext<DashboardHub> hub)
    {
        _db = db;
        _email = email;
        _hub = hub;
    }

    public async Task<AppointmentDto> BookAsync(BookAppointmentRequest request, bool pushDashboard = true, CancellationToken cancellationToken = default)
    {
        var patient = await _db.Patients.FirstOrDefaultAsync(p => p.Id == request.PatientId, cancellationToken)
            ?? throw new InvalidOperationException("Patient not found.");
        var doctor = await _db.Doctors
            .Include(d => d.Schedules)
            .FirstOrDefaultAsync(d => d.Id == request.DoctorId && d.IsActive, cancellationToken)
            ?? throw new InvalidOperationException("Doctor not found or inactive.");

        var scheduledLocal = request.ScheduledAt.Kind == DateTimeKind.Utc
            ? request.ScheduledAt.ToLocalTime()
            : request.ScheduledAt;

        EnsureAvailability(doctor, scheduledLocal);

        var overlap = await _db.Appointments.AnyAsync(a =>
            a.DoctorId == doctor.Id
            && a.Status != "Cancelled"
            && a.ScheduledAt == scheduledLocal, cancellationToken);

        if (overlap)
        {
            throw new InvalidOperationException("That slot is already booked for this doctor.");
        }

        var appointment = new Appointment
        {
            PatientId = patient.Id,
            DoctorId = doctor.Id,
            ScheduledAt = scheduledLocal,
            Status = "Scheduled",
            Notes = request.Notes ?? string.Empty,
            CreatedAt = DateTime.UtcNow
        };

        _db.Appointments.Add(appointment);
        await _db.SaveChangesAsync(cancellationToken);

        var body = $"""
            Dear Dr. {doctor.Name},

            A new appointment has been booked.

            Patient: {patient.Name}
            Age: {patient.Age}
            Contact: {patient.Contact}
            Appointment Time: {scheduledLocal:yyyy-MM-dd HH:mm}

            Regards,
            Anuj's AI Assistant
            """;

        await _email.SendAsync(doctor.Email, "New Appointment Booking", body, cancellationToken);

        if (pushDashboard)
        {
            var callLog = new CallLog
            {
                CallerName = patient.Name,
                CallerPhone = patient.Contact,
                Summary = $"Asked for {doctor.Name} appointment",
                ActionTaken = $"Booked for {scheduledLocal:h:mm tt}",
                Intent = "Appointment",
                Transcript = string.IsNullOrWhiteSpace(request.Notes) ? $"Portal booking with {doctor.Name}" : request.Notes,
                Timestamp = DateTime.UtcNow
            };
            _db.CallLogs.Add(callLog);
            await _db.SaveChangesAsync(cancellationToken);

            var dto = new CallLogDto(
                callLog.Id,
                callLog.CallerName,
                callLog.CallerPhone,
                callLog.Summary,
                callLog.ActionTaken,
                callLog.Intent,
                callLog.Transcript,
                callLog.Timestamp);
            await _hub.Clients.All.SendAsync("CallSummaryAdded", dto, cancellationToken);
        }

        return ToDto(appointment, patient, doctor);
    }

    public async Task<List<AppointmentDto>> ListAsync(int? doctorId, int? patientId, CancellationToken cancellationToken = default)
    {
        var query = _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .AsQueryable();

        if (doctorId.HasValue)
        {
            query = query.Where(a => a.DoctorId == doctorId);
        }

        if (patientId.HasValue)
        {
            query = query.Where(a => a.PatientId == patientId);
        }

        var items = await query.OrderByDescending(a => a.ScheduledAt).ToListAsync(cancellationToken);
        return items.Select(a => ToDto(a, a.Patient, a.Doctor)).ToList();
    }

    public async Task<AppointmentPageDto> ListPagedAsync(int? doctorId, int? patientId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 50 ? 8 : pageSize;

        var query = _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .AsQueryable();

        if (doctorId.HasValue)
        {
            query = query.Where(a => a.DoctorId == doctorId);
        }

        if (patientId.HasValue)
        {
            query = query.Where(a => a.PatientId == patientId);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(a => a.ScheduledAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new AppointmentPageDto(
            items.Select(a => ToDto(a, a.Patient, a.Doctor)).ToList(),
            total,
            page,
            pageSize);
    }

    public async Task<AppointmentDto> UpdateStatusAsync(int id, string status, CancellationToken cancellationToken = default)
    {
        var appointment = await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Appointment not found.");

        appointment.Status = status;
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(appointment, appointment.Patient, appointment.Doctor);
    }

    public async Task<AppointmentDto> RescheduleAsync(int id, DateTime scheduledAt, CancellationToken cancellationToken = default)
    {
        var appointment = await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .ThenInclude(d => d.Schedules)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Appointment not found.");

        var scheduledLocal = scheduledAt.Kind == DateTimeKind.Utc ? scheduledAt.ToLocalTime() : scheduledAt;
        EnsureAvailability(appointment.Doctor, scheduledLocal);
        var overlap = await _db.Appointments.AnyAsync(a =>
            a.Id != appointment.Id
            && a.DoctorId == appointment.DoctorId
            && a.Status != "Cancelled"
            && a.ScheduledAt == scheduledLocal, cancellationToken);
        if (overlap)
        {
            throw new InvalidOperationException("That slot is already booked for this doctor.");
        }

        appointment.ScheduledAt = scheduledLocal;
        appointment.Status = "Scheduled";
        await _db.SaveChangesAsync(cancellationToken);
        await _email.SendAsync(
            appointment.Doctor.Email,
            "Appointment Rescheduled",
            $"Dear Dr. {appointment.Doctor.Name},\n\n{appointment.Patient.Name} was rescheduled to {scheduledLocal:yyyy-MM-dd HH:mm}.\n\nRegards,\nAnuj's AI Assistant",
            cancellationToken);
        return ToDto(appointment, appointment.Patient, appointment.Doctor);
    }

    private static void EnsureAvailability(Doctor doctor, DateTime scheduledAt)
    {
        var day = scheduledAt.DayOfWeek;
        var time = scheduledAt.TimeOfDay;
        var match = doctor.Schedules.FirstOrDefault(s =>
            s.DayOfWeek == day && time >= s.StartTime && time < s.EndTime);

        if (match is null)
        {
            throw new InvalidOperationException($"{doctor.Name} is not available at {scheduledAt:yyyy-MM-dd HH:mm}.");
        }
    }

    public static AppointmentDto ToDto(Appointment appointment, Patient patient, Doctor doctor) =>
        new(
            appointment.Id,
            patient.Id,
            patient.Name,
            patient.Age,
            patient.Contact,
            doctor.Id,
            doctor.Name,
            doctor.Email,
            appointment.ScheduledAt,
            appointment.Status,
            appointment.Notes,
            appointment.CreatedAt);
}
