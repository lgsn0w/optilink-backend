using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using OptiLink.Hubs;
using System.Threading.Tasks;
using System;

namespace OptiLink.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TelemetryController : ControllerBase
{
    private readonly IHubContext<DashboardHub> _hub;

    public TelemetryController(IHubContext<DashboardHub> hub)
    {
        _hub = hub;
    }

    [HttpPost]
    public async Task<IActionResult> PostTelemetry([FromBody] DeviceData data)
    {
        // [DEBUG] Print to the Arch Terminal
        Console.WriteLine($"[DEBUG] SIGNAL RECEIVED: {data.DeviceId} | Battery: {data.BatteryLevel}%");
        
        await _hub.Clients.All.SendAsync("ReceiveTelemetry", data);
        return Ok();
    }
}

public record DeviceData(string DeviceId, int BatteryLevel, string Status);
