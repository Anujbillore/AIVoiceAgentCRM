using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/appointment")]
[Authorize]
public class AppointmentsController : ControllerBase
{
    private readonly IAppointmentService _appointments;
    private readonly ICurrentUserService _current;

    public AppointmentsController(IAppointmentService appointments, ICurrentUserService current)
    {
        _appointments = appointments;
        _current = current;
    }

    [HttpGet]
    public async Task<ActionResult<AppointmentPageDto>> List(
        [FromQuery] int? doctorId,
        [FromQuery] int? patientId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 8,
        CancellationToken cancellationToken = default)
    {
        if (_current.IsDoctor)
        {
            doctorId = await _current.GetDoctorIdAsync(cancellationToken);
        }

        if (_current.IsPatient)
        {
            patientId = await _current.GetPatientIdAsync(cancellationToken);
        }

        return await _appointments.ListPagedAsync(doctorId, patientId, page, pageSize, cancellationToken);
    }

    [HttpPost("book")]
    public async Task<ActionResult<AppointmentDto>> Book(BookAppointmentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (_current.IsPatient)
            {
                var ownId = await _current.GetPatientIdAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Patient profile not found.");
                request = request with { PatientId = ownId };
            }

            var appointment = await _appointments.BookAsync(request, pushDashboard: true, cancellationToken);
            return Ok(appointment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPatch("{id:int}/status")]
    public async Task<ActionResult<AppointmentDto>> UpdateStatus(int id, [FromBody] StatusRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var denied = await DenyIfNotOwnerAsync(id, cancellationToken);
            if (denied is not null)
            {
                return denied;
            }

            if (_current.IsPatient)
            {
                if (!string.Equals(body.Status, "Cancelled", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = "Patients can only cancel their own appointments." });
                }

                var own = await _appointments.ListAsync(null, await _current.GetPatientIdAsync(cancellationToken), cancellationToken);
                var current = own.FirstOrDefault(a => a.Id == id);
                if (current is not null && AppointmentStatuses.IsCompleted(current.Status))
                {
                    return BadRequest(new { message = "This visit is already completed." });
                }
            }

            return await _appointments.UpdateStatusAsync(id, body.Status, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPatch("{id:int}/reschedule")]
    public async Task<ActionResult<AppointmentDto>> Reschedule(int id, [FromBody] RescheduleRequest body, CancellationToken cancellationToken)
    {
        try
        {
            var denied = await DenyIfNotOwnerAsync(id, cancellationToken);
            if (denied is not null)
            {
                return denied;
            }

            return await _appointments.RescheduleAsync(id, body.ScheduledAt, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    public record StatusRequest(string Status);

    private async Task<ActionResult?> DenyIfNotOwnerAsync(int appointmentId, CancellationToken cancellationToken)
    {
        if (_current.IsAdmin)
        {
            return null;
        }

        if (_current.IsDoctor)
        {
            var ownId = await _current.GetDoctorIdAsync(cancellationToken);
            var list = await _appointments.ListAsync(ownId, null, cancellationToken);
            return list.Any(a => a.Id == appointmentId) ? null : Forbid();
        }

        if (_current.IsPatient)
        {
            var ownId = await _current.GetPatientIdAsync(cancellationToken);
            var list = await _appointments.ListAsync(null, ownId, cancellationToken);
            return list.Any(a => a.Id == appointmentId) ? null : Forbid();
        }

        return Forbid();
    }
}
