using Docker.DotNet;
using Docker.DotNet.Models;
using Microsoft.AspNetCore.SignalR;
using OptiLink.Hubs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace OptiLink.Services;

public class ContainerWatchdog : BackgroundService
{
    private readonly IHubContext<DashboardHub> _hub;
    private readonly ILogger<ContainerWatchdog> _logger;
    private readonly DockerClient _client;

    public ContainerWatchdog(IHubContext<DashboardHub> hub, ILogger<ContainerWatchdog> logger)
    {
        _hub = hub;
        _logger = logger;
        
        // Conecta ao Socket Unix local do Docker (Padrão Linux)
        _client = new DockerClientConfiguration(new Uri("unix:///var/run/docker.sock")).CreateClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // 1. Lista todos os containers (inclusive os parados)
                var containers = await _client.Containers.ListContainersAsync(new ContainersListParameters { All = true });
                
                var containerStats = new List<ContainerInfo>();

                foreach (var c in containers)
                {
                    // Limpa o nome (Docker retorna "/nome", removemos a barra)
                    var name = c.Names.FirstOrDefault()?.TrimStart('/') ?? "Unknown";
                    var state = c.State; // "running", "exited", etc.
                    var status = c.Status; // "Up 2 hours", "Exited (0) 5 seconds ago"

                    // Lógica de Auto-Healing (Ressuscitar Paperless se morrer)
                    if (name.Contains("paperless") && state == "exited")
                    {
                        _logger.LogWarning($"[WATCHDOG] Container {name} caiu! Tentando reiniciar...");
                        await _hub.Clients.All.SendAsync("ReceiveEvent", "⚡", $"{name} caiu. Reiniciando...", "warn");
                        
                        await _client.Containers.RestartContainerAsync(c.ID, new ContainerRestartParameters());
                        
                        await _hub.Clients.All.SendAsync("ReceiveEvent", "✔", $"{name} recuperado com sucesso.", "success");
                    }

                    containerStats.Add(new ContainerInfo(name, state, status));
                }

                // Envia a lista para o Dashboard (ainda não temos UI pra isso, mas o backend já manda)
                await _hub.Clients.All.SendAsync("ReceiveContainers", containerStats, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Erro no Docker Watchdog: {ex.Message}");
            }

            // Verifica a cada 5 segundos (não precisa ser tão rápido quanto a CPU)
            await Task.Delay(5000, stoppingToken);
        }
    }
}

// Modelo simples para enviar ao Frontend
public record ContainerInfo(string Name, string State, string Status);
