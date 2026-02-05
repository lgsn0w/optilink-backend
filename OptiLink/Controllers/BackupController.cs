using Microsoft.AspNetCore.Mvc;
using System.Diagnostics;

namespace OptiLink.Controllers;

[ApiController]
[Route("api/[controller]")]
public class BackupController : ControllerBase
{
    [HttpPost("trigger")]
    public IActionResult TriggerBackup()
    {
        // 1. O comando: Manda o container 'optilink-webserver-1' rodar o exportador
        // e salvar na pasta '../export' (que mapeamos no docker-compose)
        var fileName = "docker";
        var arguments = "exec optilink-webserver-1 document_exporter ../export";

        Console.WriteLine($"[Backup] Recebido. Tentando: {fileName} {arguments}");

        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            
            // Retorna OK rápido para não travar o App
            return Ok(new { message = "Backup iniciado no Docker." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Backup] Erro: {ex.Message}");
            // No Windows sem Docker, vai cair aqui, mas o App não trava.
            return StatusCode(500, new { error = "Falha ao executar comando", details = ex.Message });
        }
    }
}
