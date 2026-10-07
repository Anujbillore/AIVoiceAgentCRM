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
            return Forbid();
        }

        if (_current.IsPatient)
        {
            patientId = await _current.GetPatientIdAsync(cancellationToken);
            if (!patientId.HasValue)
            {
                return new AppointmentPageDto([], 0, page, pageSize);
            }
        }

        return await _appointments.ListPagedAsync(doctorId, patientId, page, pageSize, cancellationToken);
    }

    [HttpPost("book")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<AppointmentDto>> Book(BookAppointmentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (request.PatientId < 1 || request.DoctorId < 1)
            {
                return BadRequest(new { message = "Select a patient and a doctor." });
            }

            var appointment = await _appointments.BookAsync(request, pushDashboard: true, cancellationToken);
            return Ok(appointment);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost("book-new-patient")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<AppointmentDto>> BookNewPatient(BookNewPatientRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (!ModelState.IsValid)
            {
                return ValidationProblem(ModelState);
            }

            return Ok(await _appointments.BookNewPatientAsync(request, cancellationToken));
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

            if (!AppointmentStatuses.TryNormalize(body.Status, out var normalized))
            {
                return BadRequest(new { message = "Status must be Not Attended, Completed, or Cancelled." });
            }

            if (_current.IsPatient)
            {
                if (!AppointmentStatuses.IsCancelled(normalized))
                {
                    return BadRequest(new { message = "Patients can only cancel their own appointments." });
                }

                var ownId = await _current.GetPatientIdAsync(cancellationToken);
                if (!ownId.HasValue)
                {
                    return Forbid();
                }

                var own = await _appointments.ListAsync(null, ownId, cancellationToken);
                var current = own.FirstOrDefault(a => a.Id == id);
                if (current is not null && AppointmentStatuses.IsCompleted(current.Status))
                {
                    return BadRequest(new { message = "This visit is already completed." });
                }
            }

            return await _appointments.UpdateStatusAsync(id, normalized, cancellationToken);
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

    [HttpGet("{id:int}/email/{decision}")]
    [AllowAnonymous]
    public IActionResult DecideFromEmail(int id, string decision, [FromQuery] string? token)
    {
        return Content(
            EmailResultPage(
                "Approval is no longer used",
                "Doctor approve or reject is not part of booking. Appointments are stored as Not Attended until the visit is completed."),
            "text/html");
    }

    public record StatusRequest(string Status);

    private static string EmailResultPage(string title, string message)
    {
        var safeTitle = System.Net.WebUtility.HtmlEncode(title);
        var safeMessage = System.Net.WebUtility.HtmlEncode(message);
        return $"""
        <!doctype html>
        <html>
        <head><meta charset="utf-8"><title>{safeTitle} - Anuj Clinic</title></head>
        <body style="font-family:Arial,sans-serif;background:#f8fafc;padding:48px;color:#0f172a">
          <div style="max-width:480px;margin:auto;background:#fff;border-radius:16px;padding:32px;border:1px solid #e2e8f0">
            <h1 style="font-size:22px;margin:0 0 12px">{safeTitle}</h1>
            <p style="margin:0;color:#475569">{safeMessage}</p>
            <p style="margin:24px 0 0;color:#94a3b8;font-size:12px">Anuj Clinic</p>
          </div>
        </body>
        </html>
        """;
    }

    private async Task<ActionResult?> DenyIfNotOwnerAsync(int appointmentId, CancellationToken cancellationToken)
    {
        if (_current.IsAdmin)
        {
            return null;
        }

        if (_current.IsDoctor)
        {
            return Forbid();
        }

        if (_current.IsPatient)
        {
            var ownId = await _current.GetPatientIdAsync(cancellationToken);
            if (!ownId.HasValue)
            {
                return Forbid();
            }

            var list = await _appointments.ListAsync(null, ownId, cancellationToken);
            return list.Any(a => a.Id == appointmentId) ? null : Forbid();
        }

        return Forbid();
    }
}
