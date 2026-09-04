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
    [Authorize(Roles = $"{AppRoles.Admin},{AppRoles.Doctor}")]
    public async Task<ActionResult<AppointmentDto>> UpdateStatus(int id, [FromBody] StatusRequest body, CancellationToken cancellationToken)
    {
        try
        {
            if (_current.IsDoctor)
            {
                var ownId = await _current.GetDoctorIdAsync(cancellationToken);
                var list = await _appointments.ListAsync(ownId, null, cancellationToken);
                if (list.All(a => a.Id != id))
                {
                    return Forbid();
                }
            }

            return await _appointments.UpdateStatusAsync(id, body.Status, cancellationToken);
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    public record StatusRequest(string Status);
}
