using System.Security.Claims;
using AiVoicePortal.Api.Data;
using AiVoicePortal.Api.DTOs;
using AiVoicePortal.Api.Models;
using AiVoicePortal.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using System.Text;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly ITokenService _tokens;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;

    public AuthController(
        UserManager<ApplicationUser> users,
        ITokenService tokens,
        IEmailService email,
        IConfiguration config,
        AppDbContext db)
    {
        _users = users;
        _tokens = tokens;
        _email = email;
        _config = config;
        _db = db;
    }

    [HttpPost("register")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        var existing = await _users.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return BadRequest(new { message = "An account with that email already exists." });
        }

        var user = new ApplicationUser
        {
            UserName = request.Email,
            Email = request.Email,
            EmailConfirmed = true,
            FullName = request.FullName,
            Age = request.Age,
            PhoneNumber = request.Contact
        };

        var created = await _users.CreateAsync(user, request.Password);
        if (!created.Succeeded)
        {
            return BadRequest(new { message = string.Join(" ", created.Errors.Select(e => e.Description)) });
        }

        await _users.AddToRoleAsync(user, AppRoles.Patient);
        var patient = new Patient
        {
            UserId = user.Id,
            Name = request.FullName,
            Age = request.Age,
            Contact = request.Contact,
            Email = request.Email,
            Address = request.Address ?? string.Empty
        };
        _db.Patients.Add(patient);
        await _db.SaveChangesAsync();
        patient.Uhid = $"ANJ-{patient.Id:D6}";
        await _db.SaveChangesAsync();

        var token = _tokens.CreateToken(user, [AppRoles.Patient], out var expires);
        return new AuthResponse(token, user.Email!, user.FullName, AppRoles.Patient, expires);
    }

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email);
        if (user is null || !await _users.CheckPasswordAsync(user, request.Password))
        {
            return Unauthorized(new { message = "Invalid email or password." });
        }

        var roles = await _users.GetRolesAsync(user);
        var token = _tokens.CreateToken(user, roles, out var expires);
        return new AuthResponse(token, user.Email ?? request.Email, user.FullName, roles.FirstOrDefault() ?? AppRoles.Patient, expires);
    }

    [HttpPost("forgot-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email);
        if (user is not null)
        {
            var raw = await _users.GeneratePasswordResetTokenAsync(user);
            var token = WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(raw));
            var frontend = _config["FrontendUrl"] ?? "http://localhost:5173";
            var link = $"{frontend}/reset-password?email={Uri.EscapeDataString(user.Email!)}&token={token}";
            await _email.SendAsync(
                user.Email!,
                "Reset your clinic portal password",
                $"Hello {user.FullName},\n\nReset your password using this link:\n{link}\n\nIf you did not request this, ignore this email.\n");
        }

        return Ok(new { message = "If that account exists, a reset email has been sent." });
    }

    [HttpPost("reset-password")]
    [AllowAnonymous]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest request)
    {
        var user = await _users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return BadRequest(new { message = "Invalid reset request." });
        }

        var decoded = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(request.Token));
        var result = await _users.ResetPasswordAsync(user, decoded, request.NewPassword);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = string.Join(" ", result.Errors.Select(e => e.Description)) });
        }

        return Ok(new { message = "Password updated. You can sign in now." });
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthResponse>> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var user = await _users.FindByIdAsync(userId ?? string.Empty);
        if (user is null)
        {
            return Unauthorized();
        }

        var roles = await _users.GetRolesAsync(user);
        return new AuthResponse(string.Empty, user.Email ?? "", user.FullName, roles.FirstOrDefault() ?? AppRoles.Patient, DateTime.UtcNow);
    }
}
