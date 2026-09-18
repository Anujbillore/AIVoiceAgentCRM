using System.Security.Claims;
using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AiVoicePortal.Api.Services;

public interface ICurrentUserService
{
    string? UserId { get; }
    string? Email { get; }
    string Role { get; }
    bool IsAdmin { get; }
    bool IsDoctor { get; }
    bool IsPatient { get; }
    Task<int?> GetDoctorIdAsync(CancellationToken cancellationToken = default);
    Task<int?> GetPatientIdAsync(CancellationToken cancellationToken = default);
}

public class CurrentUserService : ICurrentUserService
{
    private readonly IHttpContextAccessor _http;
    private readonly AppDbContext _db;

    public CurrentUserService(IHttpContextAccessor http, AppDbContext db)
    {
        _http = http;
        _db = db;
    }

    public string? UserId => _http.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier);
    public string? Email => _http.HttpContext?.User.FindFirstValue(ClaimTypes.Email);
    public string Role => _http.HttpContext?.User.FindFirstValue(ClaimTypes.Role) ?? AppRoles.Patient;
    public bool IsAdmin => Role == AppRoles.Admin;
    public bool IsDoctor => Role == AppRoles.Doctor;
    public bool IsPatient => Role == AppRoles.Patient;

    public async Task<int?> GetDoctorIdAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(UserId))
        {
            var byUser = await _db.Doctors
                .Where(d => d.UserId == UserId)
                .Select(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byUser != 0)
            {
                return byUser;
            }
        }

        if (!string.IsNullOrWhiteSpace(Email))
        {
            var byEmail = await _db.Doctors
                .Where(d => d.Email == Email)
                .Select(d => d.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byEmail != 0)
            {
                return byEmail;
            }
        }

        return null;
    }

    public async Task<int?> GetPatientIdAsync(CancellationToken cancellationToken = default)
    {
        if (!string.IsNullOrWhiteSpace(UserId))
        {
            var byUser = await _db.Patients
                .Where(p => p.UserId == UserId)
                .Select(p => p.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byUser != 0)
            {
                return byUser;
            }
        }

        if (!string.IsNullOrWhiteSpace(Email))
        {
            var byEmail = await _db.Patients
                .Where(p => p.Email == Email)
                .Select(p => p.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (byEmail != 0)
            {
                return byEmail;
            }
        }

        return null;
    }
}
