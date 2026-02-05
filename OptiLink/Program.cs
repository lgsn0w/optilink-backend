using OptiLink.Data;
using OptiLink.Hubs;
using OptiLink.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// 1. Limpeza de Logs (Clean Logs)
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

// 2. Add Services
builder.Services.AddControllers();
builder.Services.AddSignalR();
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite("Data Source=optilink.db"));

// 3. Register Workers
builder.Services.AddHostedService<HardwareMonitor>();
builder.Services.AddHostedService<ContainerWatchdog>();

var app = builder.Build();

// 4. Database Check
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
}

// 5. MIDDLEWARE PIPELINE (AQUI ESTAVA O PROBLEMA)
app.UseDefaultFiles(); // <--- ADICIONADO: Faz o "/" virar "/index.html"
app.UseStaticFiles();  // <--- Permite servir o arquivo
app.UseRouting();

app.MapControllers();
app.MapHub<DashboardHub>("/dashboardHub");

// 6. Start
app.Run("http://0.0.0.0:5000");
