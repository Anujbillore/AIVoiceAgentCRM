using Microsoft.AspNetCore.SignalR;

namespace AiVoicePortal.Api.Hubs;

public class DashboardHub : Hub
{
    public const string Path = "/hubs/dashboard";
}
