using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AiVoicePortal.Api.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhooksController : ControllerBase
{
    private readonly IConfiguration _config;

    public WebhooksController(IConfiguration config)
    {
        _config = config;
    }

    [HttpGet("exotel/status")]
    [HttpPost("exotel/status")]
    [AllowAnonymous]
    public IActionResult ExotelStatus()
    {
        var expected = _config["Exotel:StatusToken"];
        var token = Request.Query["token"].ToString();
        if (!string.IsNullOrWhiteSpace(expected) && !string.Equals(expected, token, StringComparison.Ordinal))
        {
            return Unauthorized();
        }

        return Ok(new { received = true });
    }
}
