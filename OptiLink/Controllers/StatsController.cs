using Microsoft.AspNetCore.Mvc;
using OptiLink.Data;
using System.Linq;
using System.Collections.Generic;

namespace OptiLink.Controllers;

[ApiController]
[Route("api/[controller]")]
public class StatsController : ControllerBase
{
    private readonly AppDbContext _db;

    public StatsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("history")]
    public IActionResult GetHistory()
    {
        // Retorna apenas os últimos 100 valores de carga.
        // Sem datas. Sem confusão.
        var data = _db.CpuMetrics
            .OrderByDescending(x => x.Timestamp)
            .Take(100)
            .OrderBy(x => x.Timestamp)
            .Select(x => x.Load)
            .ToList();

        return Ok(data);
    }
}
