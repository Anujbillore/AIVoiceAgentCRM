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
    Task<AppointmentDto> DecideFromEmailAsync(int id, string action, string? token, CancellationToken cancellationToken = default);
    Task ResendBookingEmailsAsync(int id, CancellationToken cancellationToken = default);
}

public class AppointmentService : IAppointmentService
{
    private readonly AppDbContext _db;
    private readonly IEmailService _email;
    private readonly IHubContext<DashboardHub> _hub;
    private readonly IClinicNotificationService _notifications;
    private readonly IConfiguration _config;
    private readonly ILogger<AppointmentService> _logger;

    public AppointmentService(
        AppDbContext db,
        IEmailService email,
        IHubContext<DashboardHub> hub,
        IClinicNotificationService notifications,
        IConfiguration config,
        ILogger<AppointmentService> logger)
    {
        _db = db;
        _email = email;
        _hub = hub;
        _notifications = notifications;
        _config = config;
        _logger = logger;
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

        await TrySendBookingEmailsAsync(appointment, patient, doctor, scheduledLocal, cancellationToken);
        await _hub.Clients.All.SendAsync("AppointmentChanged", ToDto(appointment, patient, doctor), cancellationToken);
        await _notifications.PublishAsync(
            "New appointment booked",
            $"{patient.Name} booked with {doctor.Name} on {scheduledLocal:ddd d MMM, h:mm tt}.",
            "Appointment",
            "Staff",
            doctor.Id,
            "Appointment",
            appointment.Id,
            cancellationToken);

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

            appointment.Doctor = doctor;
            appointment.Patient = patient;
            var dto = CallLogDetails.ToDto(callLog, appointment);
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
        pageSize = pageSize is < 1 or > 200 ? 50 : pageSize;

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
        var dto = ToDto(appointment, appointment.Patient, appointment.Doctor);
        await _hub.Clients.All.SendAsync("AppointmentChanged", dto, cancellationToken);
        var title = AppointmentStatuses.IsCancelled(status)
            ? "Appointment cancelled"
            : AppointmentStatuses.IsCompleted(status)
                ? "Appointment completed"
                : "Appointment updated";
        await _notifications.PublishAsync(
            title,
            $"{appointment.Patient?.Name} · {appointment.Doctor?.Name} · {appointment.ScheduledAt:ddd d MMM, h:mm tt} · {status}",
            "Appointment",
            "Staff",
            appointment.DoctorId,
            "Appointment",
            appointment.Id,
            cancellationToken);
        return dto;
    }

    public async Task<AppointmentDto> RescheduleAsync(int id, DateTime scheduledAt, CancellationToken cancellationToken = default)
    {
        var appointment = await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .ThenInclude(d => d.Schedules)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Appointment not found.");

        if (AppointmentStatuses.IsCancelled(appointment.Status) || AppointmentStatuses.IsCompleted(appointment.Status))
        {
            throw new InvalidOperationException("This appointment can no longer be rescheduled.");
        }

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
        var dto = ToDto(appointment, appointment.Patient, appointment.Doctor);
        await _hub.Clients.All.SendAsync("AppointmentChanged", dto, cancellationToken);
        await TrySendRescheduleEmailAsync(appointment, scheduledLocal, cancellationToken);
        await _notifications.PublishAsync(
            "Appointment rescheduled",
            $"{appointment.Patient.Name} moved to {scheduledLocal:ddd d MMM, h:mm tt} with {appointment.Doctor.Name}.",
            "Appointment",
            "Staff",
            appointment.DoctorId,
            "Appointment",
            appointment.Id,
            cancellationToken);
        return dto;
    }

    public async Task<AppointmentDto> DecideFromEmailAsync(int id, string action, string? token, CancellationToken cancellationToken = default)
    {
        var secret = EmailTokenSecret();
        if (!AppointmentMail.TokenValid(id, action, token ?? "", secret))
        {
            throw new UnauthorizedAccessException("This approval link is invalid or expired.");
        }

        var normalized = action.Trim().ToLowerInvariant();
        if (normalized is not ("approve" or "reject"))
        {
            throw new InvalidOperationException("Unknown email action.");
        }

        var appointment = await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Appointment not found.");

        if (normalized == "reject" && AppointmentStatuses.IsCancelled(appointment.Status))
        {
            return ToDto(
                appointment,
                appointment.Patient ?? throw new InvalidOperationException("Patient is missing."),
                appointment.Doctor ?? throw new InvalidOperationException("Doctor is missing."));
        }

        if (normalized == "approve" && AppointmentStatuses.IsCancelled(appointment.Status))
        {
            throw new InvalidOperationException("This appointment was already cancelled.");
        }

        var nextStatus = normalized == "reject" ? "Cancelled" : "Confirmed";
        return await UpdateStatusAsync(id, nextStatus, cancellationToken);
    }

    public async Task ResendBookingEmailsAsync(int id, CancellationToken cancellationToken = default)
    {
        var appointment = await _db.Appointments
            .Include(a => a.Patient)
            .Include(a => a.Doctor)
            .FirstOrDefaultAsync(a => a.Id == id, cancellationToken)
            ?? throw new KeyNotFoundException("Appointment not found.");

        var patient = appointment.Patient ?? throw new InvalidOperationException("Patient is missing.");
        var doctor = appointment.Doctor ?? throw new InvalidOperationException("Doctor is missing.");
        await TrySendBookingEmailsAsync(appointment, patient, doctor, appointment.ScheduledAt, cancellationToken, requireDelivery: true);
    }

    private async Task TrySendBookingEmailsAsync(
        Appointment appointment,
        Patient patient,
        Doctor doctor,
        DateTime scheduledLocal,
        CancellationToken cancellationToken,
        bool requireDelivery = false)
    {
        try
        {
            var patientName = PatientDisplayName(patient);
            var (approveUrl, rejectUrl) = EmailDecisionUrls(appointment.Id);
            var doctorBody = AppointmentMail.DoctorBookingBody(patientName, scheduledLocal, approveUrl, rejectUrl);
            await _email.SendDoctorAsync(
                doctor.Email,
                "New Appointment Request",
                doctorBody,
                cancellationToken,
                AppointmentMail.ToHtml(doctorBody, approveUrl, rejectUrl),
                requireDelivery);

            var patientBody = AppointmentMail.PatientBookingBody(
                patientName,
                AppointmentMail.DisplayDoctor(doctor),
                scheduledLocal,
                ClinicAddress());
            await _email.SendPatientAsync(
                patient.Email,
                "Appointment Confirmation",
                patientBody,
                cancellationToken,
                AppointmentMail.ToHtml(patientBody),
                requireDelivery);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Booking emails failed for appointment {AppointmentId}", appointment.Id);
            if (requireDelivery)
            {
                throw;
            }
        }
    }

    private async Task TrySendRescheduleEmailAsync(Appointment appointment, DateTime scheduledLocal, CancellationToken cancellationToken)
    {
        try
        {
            var body = AppointmentMail.DoctorRescheduleBody(PatientDisplayName(appointment.Patient), scheduledLocal);
            await _email.SendDoctorAsync(
                appointment.Doctor.Email,
                "Appointment Rescheduled",
                body,
                cancellationToken,
                AppointmentMail.ToHtml(body));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Reschedule email failed for appointment {AppointmentId}", appointment.Id);
        }
    }

    private string PatientDisplayName(Patient? patient)
    {
        var overrideOn = !bool.TryParse(_config["Email:OverrideRecipients"], out var flag) || flag;
        var testName = _config["Email:TestPatientName"];
        if (overrideOn && !string.IsNullOrWhiteSpace(testName))
        {
            return testName.Trim();
        }

        return patient is null ? "Patient" : AppointmentMail.DisplayName(patient);
    }

    private (string ApproveUrl, string RejectUrl) EmailDecisionUrls(int appointmentId)
    {
        var api = (_config["Email:PublicApiUrl"] ?? _config["Voice:PublicBaseUrl"] ?? "http://localhost:5147").TrimEnd('/');
        var secret = EmailTokenSecret();
        var approve = AppointmentMail.Token(appointmentId, "approve", secret);
        var reject = AppointmentMail.Token(appointmentId, "reject", secret);
        return (
            $"{api}/api/appointment/{appointmentId}/email/approve?token={approve}",
            $"{api}/api/appointment/{appointmentId}/email/reject?token={reject}");
    }

    private string EmailTokenSecret() =>
        _config["Email:TokenSecret"] ?? _config["Jwt:Key"] ?? "AnujClinic-appointment-email-secret";

    private string ClinicAddress() =>
        _config["Email:ClinicAddress"] ?? "Anuj Clinic, Andheri, Mumbai";

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
