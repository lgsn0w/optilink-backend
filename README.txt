################################################################################
#                                                                              #
#    O P T I L I N K   :   P R O J E C T   A R C H I V E   ( S E R V E R )     #
#                                                                              #
#    STATUS:  ONLINE / STABLE                                                  #
#    SYSTEM:  ARCH LINUX (THE FORGE)                                           #
#    FRAMEWORK: .NET 10.0 (ASP.NET CORE)                                       #
#                                                                              #
################################################################################

================================================================================
| SECTION 1: SYSTEM ARCHITECTURE
================================================================================

[ROOT PATH] : ~/Projects/OptiLink/OptiLink/

[FILE TREE]
├── Controllers
│   └── TelemetryController.cs   <-- Receives Phone Data via HTTP POST
├── Hubs
│   └── DashboardHub.cs          <-- Manages WebSocket Connections
├── Services
│   ├── ContainerWatchdog.cs     <-- Auto-restarts crashed Docker containers
│   └── HardwareMonitor.cs       <-- Reads /proc/meminfo & /proc/loadavg
├── wwwroot
│   └── index.html               <-- The "Matrix" Web Dashboard
├── OptiLink.csproj              <-- Project Config & Dependencies
├── Program.cs                   <-- The Application Bootstrap / Wiring
└── appsettings.json             <-- Config (Default)

================================================================================
| SECTION 2: THE CONFIGURATION (.csproj)
| NOTE: Fixed Arch Linux Restore Error & Version Mismatches
================================================================================
FILE: OptiLink.csproj
---------------------
<Project Sdk="Microsoft.NET.Sdk.Web">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AllowMissingPrunePackageData>true</AllowMissingPrunePackageData>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Docker.DotNet" Version="3.125.15" />
  </ItemGroup>

</Project>

================================================================================
| SECTION 3: THE BOOTSTRAP (Program.cs)
| NOTE: Wires up the Watchdog, Hardware Monitor, and SignalR
================================================================================
FILE: Program.cs
----------------
using OptiLink.Hubs;
using OptiLink.Controllers;
using OptiLink.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. ADD CORE SERVICES
builder.Services.AddSignalR();
builder.Services.AddControllers();

// 2. REGISTER BACKGROUND SERVICES (The "Brains")
// The Watchdog: Keeps Docker Containers Alive
builder.Services.AddHostedService<ContainerWatchdog>();
// The Monitor: Reads Linux Kernel Vitals
builder.Services.AddHostedService<HardwareMonitor>();

// 3. CONFIGURE NETWORK SECURITY (CORS)
builder.Services.AddCors(options => {
    options.AddDefaultPolicy(policy => {
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
    });
});

var app = builder.Build();

// 4. BIND TO ALL IPs (Allows Phone Access)
app.Urls.Add("http://0.0.0.0:5000");

// 5. MIDDLEWARE PIPELINE
app.UseCors();
app.UseDefaultFiles(); // Serves index.html by default
app.UseStaticFiles();  // Enables wwwroot access

// 6. MAP ENDPOINTS
app.MapControllers();
app.MapHub<DashboardHub>("/dashboardHub");

app.Run();

================================================================================
| SECTION 4: THE SELF-HEALING WATCHDOG
| NOTE: Connects to Unix Socket, Scans every 5s, Restarts on Exit.
================================================================================
FILE: Services/ContainerWatchdog.cs
-----------------------------------
using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.SignalR;
using OptiLink.Hubs;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace OptiLink.Services;

public class ContainerWatchdog : BackgroundService
{
    private readonly DockerClient _client;
    private readonly IHubContext<DashboardHub> _hub;
    private const string TargetContainer = "nginx-test";

    public ContainerWatchdog(IHubContext<DashboardHub> hub)
    {
        _hub = hub;
        _client = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Console.WriteLine("[SYSTEM] Watchdog Service STARTED.");
        
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                Console.Write("."); // Heartbeat in Terminal

                var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters { All = true });
                var target = containers.FirstOrDefault(c => c.Names.Contains("/" + TargetContainer));

                if (target != null)
                {
                    if (target.State == "exited")
                    {
                        Console.WriteLine($"\n[ALERT] {TargetContainer} is DOWN! State: {target.State}. RESTARTING NOW...");
                        
                        await _client.Containers.RestartContainerAsync(target.ID, new ContainerRestartParameters());
                        await _hub.Clients.All.SendAsync("ReceiveLog", $"[FIXED] {TargetContainer} restarted.");
                        
                        Console.WriteLine($"[SUCCESS] {TargetContainer} is back online.");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n[ERROR] Watchdog Crash: {ex.Message}");
            }

            await Task.Delay(5000, stoppingToken);
        }
    }
}

================================================================================
| SECTION 5: THE SYSTEM INTERNALS
| NOTE: Reads /proc/meminfo for RAM and /proc/loadavg for CPU
================================================================================
FILE: Services/HardwareMonitor.cs
---------------------------------
using Microsoft.AspNetCore.SignalR;
using OptiLink.Hubs;
using Microsoft.Extensions.Hosting;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;

namespace OptiLink.Services;

public class HardwareMonitor : BackgroundService
{
    private readonly IHubContext<DashboardHub> _hub;

    public HardwareMonitor(IHubContext<DashboardHub> hub)
    {
        _hub = hub;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var stats = GetLinuxMetrics();
            await _hub.Clients.All.SendAsync("ReceiveStats", stats);
            await Task.Delay(2000, stoppingToken); 
        }
    }

    private SystemStats GetLinuxMetrics()
    {
        try
        {
            var memInfo = File.ReadAllText("/proc/meminfo");
            var totalLine = memInfo.Split('\n').FirstOrDefault(l => l.StartsWith("MemTotal:"));
            var availLine = memInfo.Split('\n').FirstOrDefault(l => l.StartsWith("MemAvailable:"));

            long total = ParseMem(totalLine);
            long avail = ParseMem(availLine);
            long used = total - avail;
            double percent = (double)used / total * 100;

            var loadAvg = File.ReadAllText("/proc/loadavg").Split(' ')[0];

            return new SystemStats($"{percent:0.0}", $"{used / 1024} MB", loadAvg);
        }
        catch
        {
            return new SystemStats("0", "0 MB", "0.00");
        }
    }

    private long ParseMem(string line)
    {
        if (string.IsNullOrEmpty(line)) return 0;
        var parts = line.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return 0;
        return long.Parse(parts[1]); 
    }
}

public record SystemStats(string MemoryPercent, string MemoryUsed, string CpuLoad);

================================================================================
| SECTION 6: THE FACE (Web UI)
| NOTE: Dark Mode, Matrix Green, WebSocket Client
================================================================================
FILE: wwwroot/index.html
------------------------
<!DOCTYPE html>
<html>
<head>
    <title>OPTILINK EDGE</title>
    <style>
        body { background-color: #000; color: #0f0; font-family: 'Courier New', monospace; padding: 20px; }
        h1 { border-bottom: 2px solid #0f0; padding-bottom: 10px; }
        .grid { display: grid; grid-template-columns: 1fr 1fr; gap: 20px; }
        .box { border: 1px solid #0f0; padding: 15px; min-height: 150px; }
        .log-console { border: 1px solid #333; color: #888; height: 200px; overflow-y: scroll; padding: 10px; font-size: 12px; margin-top: 20px; }
        .blink { animation: blinker 1s linear infinite; }
        @keyframes blinker { 50% { opacity: 0; } }
        .stat-val { font-size: 2em; font-weight: bold; }
        .highlight { color: #fff; }
    </style>
    <script src="https://cdnjs.cloudflare.com/ajax/libs/microsoft-signalr/8.0.0/signalr.min.js"></script>
</head>
<body>
    <h1>OPTILINK // EDGE_ORCHESTRATOR <span class="blink">_</span></h1>

    <div class="grid">
        <div class="box">
            <h3>>> SYSTEM VITALS</h3>
            <p>RAM USAGE: <span id="sys-ram-pct" class="stat-val">--</span>%</p>
            <p>USED: <span id="sys-ram-used" class="highlight">-- MB</span></p>
            <p>CPU LOAD (1m): <span id="sys-load" class="highlight">--</span></p>
        </div>

        <div class="box">
            <h3>>> SENSOR TELEMETRY</h3>
            <p>ID: <span id="dev-id">WAITING...</span></p>
            <p>BATTERY: <span id="dev-bat" class="stat-val">--</span>%</p>
            <p>STATUS: <span id="dev-status">NO DATA</span></p>
        </div>
    </div>

    <div class="log-console" id="logs">
        <div>[SYSTEM] Dashboard Initialized... Listening on Port 5000...</div>
    </div>

    <script>
        const connection = new signalR.HubConnectionBuilder()
            .withUrl("/dashboardHub")
            .build();

        connection.on("ReceiveLog", (message) => {
            const logs = document.getElementById("logs");
            const div = document.createElement("div");
            div.textContent = `[${new Date().toLocaleTimeString()}] > ${message}`;
            if(message.includes("CRITICAL") || message.includes("ALERT")) div.style.color = "red";
            if(message.includes("SUCCESS") || message.includes("FIXED")) div.style.color = "#0f0";
            logs.prepend(div);
        });

        connection.on("ReceiveStats", (stats) => {
            document.getElementById("sys-ram-pct").innerText = stats.memoryPercent;
            document.getElementById("sys-ram-used").innerText = stats.memoryUsed;
            document.getElementById("sys-load").innerText = stats.cpuLoad;
        });

        connection.on("ReceiveTelemetry", (data) => {
            document.getElementById("dev-id").innerText = data.deviceId;
            document.getElementById("dev-bat").innerText = data.batteryLevel;
            document.getElementById("dev-status").innerText = data.status;
            
            const logs = document.getElementById("logs");
            const div = document.createElement("div");
            div.textContent = `[DATA] Packet received from ${data.deviceId}`;
            div.style.color = "#555";
            logs.prepend(div);
        });

        connection.start().catch(err => console.error(err));
    </script>
</body>
</html>

================================================================================
| END OF ARCHIVE
################################################################################
