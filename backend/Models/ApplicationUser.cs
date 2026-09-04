using Microsoft.AspNetCore.Identity;

namespace AiVoicePortal.Api.Models;

public class ApplicationUser : IdentityUser
{
    public string FullName { get; set; } = string.Empty;
    public int? Age { get; set; }
}
