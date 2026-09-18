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
            if (!doctorId.HasValue)
            {
                return new AppointmentPageDto([], 0, page, pageSize);
            }
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
    public async Task<ActionResult<AppointmentDto>> Book(BookAppointmentRequest request, CancellationToken cancellationToken)
    {
        try
        {
            if (_current.IsDoctor)
            {
                var ownDoctorId = await _current.GetDoctorIdAsync(cancellationToken)
                    ?? throw new InvalidOperationException("Doctor profile not found.");
                request = request with { DoctorId = ownDoctorId };
            }

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

    [HttpGet("{id:int}/email/{decision}")]
    [AllowAnonymous]
    public async Task<IActionResult> DecideFromEmail(int id, string decision, [FromQuery] string? token, CancellationToken cancellationToken)
    {
        try
        {
            if (!string.Equals(decision, "approve", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(decision, "reject", StringComparison.OrdinalIgnoreCase))
            {
                return new ContentResult
                {
                    StatusCode = 400,
                    ContentType = "text/html",
                    Content = EmailResultPage("Unknown action", "This appointment link is not valid.")
                };
            }

            await _appointments.DecideFromEmailAsync(id, decision, token, cancellationToken);
            var approved = decision.Equals("approve", StringComparison.OrdinalIgnoreCase);
            return Content(
                EmailResultPage(
                    approved ? "Appointment approved" : "Appointment rejected",
                    approved
                        ? "The appointment is confirmed. The clinic portal has been updated."
                        : "The appointment has been cancelled."),
                "text/html");
        }
        catch (UnauthorizedAccessException)
        {
            return Content(EmailResultPage("Invalid link", "This approval link is invalid or expired."), "text/html");
        }
        catch (KeyNotFoundException)
        {
            return Content(EmailResultPage("Not found", "That appointment no longer exists."), "text/html");
        }
        catch (InvalidOperationException ex)
        {
            return Content(EmailResultPage("Could not update", ex.Message), "text/html");
        }
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
            var ownId = await _current.GetDoctorIdAsync(cancellationToken);
            if (!ownId.HasValue)
            {
                return Forbid();
            }

            var list = await _appointments.ListAsync(ownId, null, cancellationToken);
            return list.Any(a => a.Id == appointmentId) ? null : Forbid();
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
