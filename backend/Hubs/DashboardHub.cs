using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace AiVoicePortal.Api.Hubs;

[Authorize]
public class DashboardHub : Hub
{
    public const string Path = "/hubs/dashboard";
}
