using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using AiVoicePortal.Api.Models;
using Microsoft.IdentityModel.Tokens;

namespace AiVoicePortal.Api.Services;

public interface ITokenService
{
    string CreateToken(ApplicationUser user, IList<string> roles, out DateTime expiresAt);
}

public class TokenService : ITokenService
{
    private readonly IConfiguration _config;

    public TokenService(IConfiguration config)
    {
        _config = config;
    }

    public string CreateToken(ApplicationUser user, IList<string> roles, out DateTime expiresAt)
    {
        var key = _config["Jwt:Key"] ?? throw new InvalidOperationException("Jwt:Key is missing.");
        var issuer = _config["Jwt:Issuer"] ?? "AiVoicePortal";
        var audience = _config["Jwt:Audience"] ?? "AiVoicePortal";
        var hours = int.TryParse(_config["Jwt:ExpiresHours"], out var h) ? h : 8;

        expiresAt = DateTime.UtcNow.AddHours(hours);
        var role = roles.FirstOrDefault() ?? AppRoles.Patient;

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id),
            new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),
            new(ClaimTypes.NameIdentifier, user.Id),
            new(ClaimTypes.Email, user.Email ?? string.Empty),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Role, role)
        };

        var credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(key)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer,
            audience,
            claims,
            expires: expiresAt,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
