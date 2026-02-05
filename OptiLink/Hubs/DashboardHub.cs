using Microsoft.AspNetCore.SignalR;
using System.Threading.Tasks;

namespace OptiLink.Hubs;

public class DashboardHub : Hub
{
    public async Task RequestUpdate()
    {
        await Clients.All.SendAsync("ReceiveLog", "Dashboard Requested Manual Update.");
    }
}
