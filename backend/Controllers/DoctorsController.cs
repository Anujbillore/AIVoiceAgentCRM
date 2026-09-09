using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/doctors")]
[Authorize]
public class DoctorsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly UserManager<ApplicationUser> _users;

    public DoctorsController(AppDbContext db, UserManager<ApplicationUser> users)
    {
        _db = db;
        _users = users;
    }

    [HttpGet]
    public async Task<ActionResult<List<DoctorDto>>> List(CancellationToken cancellationToken)
    {
        var doctors = await _db.Doctors.Include(d => d.Schedules).OrderBy(d => d.Name).ToListAsync(cancellationToken);
        return doctors.Select(ToDto).ToList();
    }

    [HttpPost]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<DoctorDto>> Create(DoctorRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Set a password so this doctor can sign in with their email." });
        }

        if (request.Schedules is null || request.Schedules.Count == 0)
        {
            return BadRequest(new { message = "Select at least one working day." });
        }

        if (!DoctorSpecialties.IsKnown(request.Specialization))
        {
            return BadRequest(new { message = "Select a specialization from the list." });
        }

        var doctor = new Doctor
        {
            Name = request.Name,
            Email = request.Email,
            Specialization = request.Specialization.Trim(),
            Phone = request.Phone ?? string.Empty,
            IsActive = request.IsActive,
            Schedules = MapSchedules(request.Schedules)
        };

        var login = await EnsureDoctorLoginAsync(doctor, request.Password);
        if (login is not null)
        {
            return BadRequest(new { message = login });
        }

        _db.Doctors.Add(doctor);
        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(doctor);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<ActionResult<DoctorDto>> Update(int id, DoctorRequest request, CancellationToken cancellationToken)
    {
        var doctor = await _db.Doctors.Include(d => d.Schedules).FirstOrDefaultAsync(d => d.Id == id, cancellationToken);
        if (doctor is null)
        {
            return NotFound();
        }

        if (request.Schedules is null || request.Schedules.Count == 0)
        {
            return BadRequest(new { message = "Select at least one working day." });
        }

        if (!DoctorSpecialties.IsKnown(request.Specialization))
        {
            return BadRequest(new { message = "Select a specialization from the list." });
        }

        doctor.Name = request.Name;
        doctor.Email = request.Email;
        doctor.Specialization = request.Specialization.Trim();
        doctor.Phone = request.Phone ?? string.Empty;
        doctor.IsActive = request.IsActive;
        _db.DoctorSchedules.RemoveRange(doctor.Schedules);
        doctor.Schedules = MapSchedules(request.Schedules);

        var login = await EnsureDoctorLoginAsync(doctor, request.Password);
        if (login is not null)
        {
            return BadRequest(new { message = login });
        }

        await _db.SaveChangesAsync(cancellationToken);
        return ToDto(doctor);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = AppRoles.Admin)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var doctor = await _db.Doctors.FindAsync([id], cancellationToken);
        if (doctor is null)
        {
            return NotFound();
        }

        doctor.IsActive = false;
        await _db.SaveChangesAsync(cancellationToken);
        return NoContent();
    }

    private async Task<string?> EnsureDoctorLoginAsync(Doctor doctor, string? password)
    {
        ApplicationUser? user = null;
        if (!string.IsNullOrEmpty(doctor.UserId))
        {
            user = await _users.FindByIdAsync(doctor.UserId);
        }

        user ??= await _users.FindByEmailAsync(doctor.Email);

        if (user is null)
        {
            if (string.IsNullOrWhiteSpace(password))
            {
                return "Set a password so this doctor can sign in with their Gmail.";
            }

            user = new ApplicationUser
            {
                UserName = doctor.Email,
                Email = doctor.Email,
                EmailConfirmed = true,
                FullName = doctor.Name,
                PhoneNumber = doctor.Phone
            };
            var created = await _users.CreateAsync(user, password);
            if (!created.Succeeded)
            {
                return string.Join(" ", created.Errors.Select(e => e.Description));
            }

            if (!await _users.IsInRoleAsync(user, AppRoles.Doctor))
            {
                await _users.AddToRoleAsync(user, AppRoles.Doctor);
            }
        }
        else
        {
            var roles = await _users.GetRolesAsync(user);
            if (roles.Contains(AppRoles.Admin) || roles.Contains(AppRoles.Patient))
            {
                return "That email is already used by another account.";
            }

            user.Email = doctor.Email;
            user.UserName = doctor.Email;
            user.FullName = doctor.Name;
            user.PhoneNumber = doctor.Phone;
            var updated = await _users.UpdateAsync(user);
            if (!updated.Succeeded)
            {
                return string.Join(" ", updated.Errors.Select(e => e.Description));
            }

            if (!await _users.IsInRoleAsync(user, AppRoles.Doctor))
            {
                await _users.AddToRoleAsync(user, AppRoles.Doctor);
            }

            if (!string.IsNullOrWhiteSpace(password))
            {
                var token = await _users.GeneratePasswordResetTokenAsync(user);
                var reset = await _users.ResetPasswordAsync(user, token, password);
                if (!reset.Succeeded)
                {
                    return string.Join(" ", reset.Errors.Select(e => e.Description));
                }
            }
        }

        doctor.UserId = user.Id;
        return null;
    }

    private static List<DoctorSchedule> MapSchedules(List<ScheduleRequest>? schedules)
    {
        if (schedules is null || schedules.Count == 0)
        {
            return [];
        }

        return schedules.Select(s => new DoctorSchedule
        {
            DayOfWeek = s.DayOfWeek,
            StartTime = TimeSpan.Parse(s.StartTime),
            EndTime = TimeSpan.Parse(s.EndTime)
        }).ToList();
    }

    private static DoctorDto ToDto(Doctor d) =>
        new(
            d.Id,
            d.Name,
            d.Email,
            d.Specialization,
            d.Phone,
            d.IsActive,
            !string.IsNullOrEmpty(d.UserId),
            d.Schedules
                .OrderBy(s => s.DayOfWeek)
                .Select(s => new ScheduleDto(s.Id, s.DayOfWeek, s.StartTime.ToString(@"hh\:mm"), s.EndTime.ToString(@"hh\:mm")))
                .ToList());
}
