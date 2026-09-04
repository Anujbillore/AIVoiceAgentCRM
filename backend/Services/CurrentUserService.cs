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
        var id = await _db.Doctors
            .Where(d => (UserId != null && d.UserId == UserId) || (Email != null && d.Email == Email))
            .Select(d => d.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id == 0 ? null : id;
    }

    public async Task<int?> GetPatientIdAsync(CancellationToken cancellationToken = default)
    {
        var id = await _db.Patients
            .Where(p => (UserId != null && p.UserId == UserId) || (Email != null && p.Email == Email))
            .Select(p => p.Id)
            .FirstOrDefaultAsync(cancellationToken);
        return id == 0 ? null : id;
    }
}
