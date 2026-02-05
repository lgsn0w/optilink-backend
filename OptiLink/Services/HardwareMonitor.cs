using Microsoft.AspNetCore.SignalR;
using OptiLink.Hubs;
using OptiLink.Data;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Threading;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Diagnostics;
using System.Collections.Generic;

namespace OptiLink.Services;

public class HardwareMonitor : BackgroundService
{
    private readonly IHubContext<DashboardHub> _hub;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HardwareMonitor> _logger;
    
    // Variáveis de Estado (Deltas)
    private long _lastBytesIn = 0;
    private long _lastBytesOut = 0;
    private DateTime _lastNetCheck = DateTime.MinValue;
    
    private long _lastSectorsRead = 0;
    private long _lastSectorsWritten = 0;
    private DateTime _lastDiskCheck = DateTime.MinValue;

    private bool _wasHighLoad = false;
    private bool _wasSshDown = false;

    public HardwareMonitor(IHubContext<DashboardHub> hub, IServiceScopeFactory scopeFactory, ILogger<HardwareMonitor> logger)
    {
        _hub = hub;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

   protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try 
            {
                var stats = await CollectSystemMetricsAsync();
                try { await SaveMetricsAsync(stats.CpuLoad, stoppingToken); } catch {}
                await _hub.Clients.All.SendAsync("ReceiveStats", stats, stoppingToken);
                await CheckForEvents(stats);
            }
            catch (Exception ex) { _logger.LogError(ex.Message); }
            await Task.Delay(2000, stoppingToken);
        }
    } 

    private async Task<SystemStats> CollectSystemMetricsAsync()
    {
        // 1. Memória
        var memInfo = ParseMemInfo();
        long totalMem = memInfo["MemTotal"];
        long availMem = memInfo["MemAvailable"];
        double ramPct = CalculatePercentage(totalMem - availMem, totalMem);
        double ramUsedGb = (totalMem - availMem) / 1024.0 / 1024.0;

        // 2. CPU & Load
        string l1 = "0.00";
        if (File.Exists("/proc/loadavg")) l1 = File.ReadAllText("/proc/loadavg").Split(' ')[0];
        string cpuTemp = GetCpuTemperature();

        // 3. I/O (Rede & Disco)
        var netStats = CalculateNetworkMetrics();
        var diskIo = CalculateDiskIo(); // NOVA
        var ping = await GetPingAsync();
        
        bool sshUp = IsPortOpen(22);
        bool webUp = IsPortOpen(5000);
        int processCount = GetProcessCount();
        
        // 4. Uptime & Boot
        string uptimeStr = "0h";
        string bootTimeStr = "--/-- --:--";
        if (File.Exists("/proc/uptime")) {
             var uptimeRaw = File.ReadAllText("/proc/uptime").Split(' ')[0];
             if (double.TryParse(uptimeRaw, out var s)) {
                var t = TimeSpan.FromSeconds(s);
                uptimeStr = $"{(int)t.TotalDays}d {t.Hours}h {t.Minutes}m";
                bootTimeStr = DateTime.Now.Subtract(t).ToString("dd/MM HH:mm");
             }
        }

        // 5. Inteligência
        double loadVal = double.TryParse(l1, out var v) ? v : 0;
        var (healthScore, healthVerdict) = CalculateHealth(loadVal, ramPct, sshUp, webUp);
        string sysState = DetermineSystemState(loadVal, ramPct, sshUp);

        return new SystemStats(
            $"{ramPct:F1}", l1, $"{ramUsedGb:F1}", uptimeStr, bootTimeStr,
            $"{netStats.SpeedIn:F1}", $"{netStats.SpeedOut:F1}", netStats.TotalIn,
            $"{diskIo.ReadMb:F1}", $"{diskIo.WriteMb:F1}", // Novos Campos
            cpuTemp, ping, sshUp, webUp, processCount,
            healthScore, healthVerdict, sysState
        );
    }

    // --- CÁLCULO DE DISK I/O (NOVO) ---
    private (double ReadMb, double WriteMb) CalculateDiskIo()
    {
        try {
            if (!File.Exists("/proc/diskstats")) return (0, 0);
            
            // Procura o disco principal (sda, nvme0n1, ou vda)
            var lines = File.ReadAllLines("/proc/diskstats");
            long secRead = 0, secWrite = 0;

            foreach (var line in lines)
            {
                var parts = line.Trim().Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                // Filtra partições (pega apenas devices físicos como sda, nvme0n1)
                if (parts.Length > 10 && !parts[2].StartsWith("loop") && !parts[2].StartsWith("ram") && char.IsDigit(parts[2].Last())) continue;
                
                // Soma atividade de todos os discos físicos encontrados
                if (parts.Length > 10 && (parts[2].StartsWith("sd") || parts[2].StartsWith("nvme") || parts[2].StartsWith("vd")))
                {
                    secRead += long.Parse(parts[5]);  // Setores lidos
                    secWrite += long.Parse(parts[9]); // Setores escritos
                }
            }

            var now = DateTime.Now;
            double interval = (now - _lastDiskCheck).TotalSeconds;
            if (interval < 0.1) interval = 2.0;

            // 1 Setor = 512 bytes (Geralmente)
            double readMb = _lastSectorsRead == 0 ? 0 : (secRead - _lastSectorsRead) * 512.0 / 1024.0 / 1024.0 / interval;
            double writeMb = _lastSectorsWritten == 0 ? 0 : (secWrite - _lastSectorsWritten) * 512.0 / 1024.0 / 1024.0 / interval;

            _lastDiskCheck = now;
            _lastSectorsRead = secRead;
            _lastSectorsWritten = secWrite;

            return (readMb, writeMb);
        }
        catch { return (0, 0); }
    }

    // --- Helpers Existentes (Compactados) ---
    private (int, string) CalculateHealth(double load, double ram, bool ssh, bool web) {
        int score = 100;
        if (load > 1.0) score -= (int)((load - 1.0) * 20);
        if (ram > 80) score -= 10;
        if (!ssh) score -= 5;
        if (score < 0) score = 0;
        string verdict = score >= 90 ? "Excelente" : score >= 70 ? "Bom" : score >= 50 ? "Atenção" : "Crítico";
        return (score, verdict);
    }
    private string DetermineSystemState(double load, double ram, bool ssh) {
        if (!ssh) return "Falha de Serviço";
        if (load > 2.0) return "Sobrecarga CPU";
        if (ram > 90) return "Memória Cheia";
        if (load > 0.8) return "Carga Sustentada";
        return "Estável";
    }
    private async Task CheckForEvents(SystemStats s) {
        double load = double.Parse(s.CpuLoad);
        if (load > 1.5 && !_wasHighLoad) { await _hub.Clients.All.SendAsync("ReceiveEvent", "⚠", "Pico de Carga (>1.5)", "warn"); _wasHighLoad = true; }
        else if (load < 1.0 && _wasHighLoad) { await _hub.Clients.All.SendAsync("ReceiveEvent", "✔", "Carga Estabilizada", "success"); _wasHighLoad = false; }
        if (!s.SshUp && !_wasSshDown) { await _hub.Clients.All.SendAsync("ReceiveEvent", "⚡", "SSH Caiu", "danger"); _wasSshDown = true; }
        else if (s.SshUp && _wasSshDown) { await _hub.Clients.All.SendAsync("ReceiveEvent", "✔", "SSH Recuperado", "success"); _wasSshDown = false; }
    }
    private int GetProcessCount() { try { return Directory.GetDirectories("/proc").Count(d => int.TryParse(Path.GetFileName(d), out _)); } catch { return 0; } }
    private (double SpeedIn, double SpeedOut, string TotalIn, string TotalOut) CalculateNetworkMetrics() { try { if (!File.Exists("/proc/net/dev")) return (0, 0, "0 B", "0 B"); var lines = File.ReadAllLines("/proc/net/dev"); long tin = 0, tout = 0; foreach(var line in lines.Skip(2)) { var parts = line.Trim().Split(new[]{' '}, StringSplitOptions.RemoveEmptyEntries); if(parts.Length > 9 && !parts[0].StartsWith("lo")) { tin += long.Parse(parts[1]); tout += long.Parse(parts[9]); } } var now = DateTime.Now; double interval = (now - _lastNetCheck).TotalSeconds; if(interval < 0.1) interval = 2.0; double inKb = _lastBytesIn == 0 ? 0 : (tin - _lastBytesIn) / 1024.0 / interval; double outKb = _lastBytesOut == 0 ? 0 : (tout - _lastBytesOut) / 1024.0 / interval; _lastNetCheck = now; _lastBytesIn = tin; _lastBytesOut = tout; return (inKb, outKb, FormatBytes(tin), FormatBytes(tout)); } catch { return (0, 0, "0 B", "0 B"); } }
    private string FormatBytes(long bytes) { if (bytes > 1024L * 1024 * 1024) return $"{(bytes / 1073741824.0):F1} GB"; return $"{(bytes / 1048576.0):F0} MB"; }
    private async Task<string> GetPingAsync() { try { using var pinger = new Ping(); var reply = await pinger.SendPingAsync("1.1.1.1", 1000); return reply.Status == IPStatus.Success ? reply.RoundtripTime.ToString() : "Err"; } catch { return "Err"; } }
    private bool IsPortOpen(int port) { try { using var client = new TcpClient(); var result = client.BeginConnect("127.0.0.1", port, null, null); return result.AsyncWaitHandle.WaitOne(100); } catch { return false; } }
    private string GetCpuTemperature() { try { var tempFiles = Directory.GetFiles("/sys/class/thermal/", "thermal_zone*"); foreach (var zone in tempFiles) { var type = File.ReadAllText(Path.Combine(zone, "type")).Trim(); if (type == "x86_pkg_temp" || type == "acpitz") { var t = double.Parse(File.ReadAllText(Path.Combine(zone, "temp")).Trim()); return (t / 1000).ToString("F0"); } } return "--"; } catch { return "--"; } }
    private Dictionary<string, long> ParseMemInfo() { var d = new Dictionary<string, long>(); try { foreach(var l in File.ReadAllLines("/proc/meminfo")) { var p = l.Split(':', StringSplitOptions.RemoveEmptyEntries); if(p.Length > 1) d[p[0].Trim()] = long.Parse(p[1].Trim().Split(' ')[0]); } } catch {} if(!d.ContainsKey("MemTotal")) d["MemTotal"] = 1; return d; }
    private double CalculatePercentage(long used, long total) => total <= 0 ? 0 : Math.Round((used / (double)total) * 100, 1);
    private async Task SaveMetricsAsync(string cpuLoadStr, CancellationToken ct) { if (!double.TryParse(cpuLoadStr, out var cpuLoad)) return; using var scope = _scopeFactory.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); db.CpuMetrics.Add(new CpuMetric { Timestamp = DateTime.UtcNow, Load = cpuLoad }); await db.SaveChangesAsync(ct); }
}

public record SystemStats(
    string MemoryPercent, string CpuLoad, string RamUsedGb, 
    string Uptime, string BootTime, 
    string NetIn, string NetOut, string TotalIn,
    string DiskReadMb, string DiskWriteMb, // Novos
    string CpuTemp, string Ping, bool SshUp, bool WebUp, int ProcessCount,
    int HealthScore, string HealthVerdict, string SystemState
)
{
    public static SystemStats Default => new("0", "0.00", "0.0", "0h", "-", "0", "0", "0B", "0", "0", "--", "--", false, false, 0, 100, "Calc", "Init");
};
