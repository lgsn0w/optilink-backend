using Microsoft.EntityFrameworkCore;
using System;

namespace OptiLink.Data;

public class AppDbContext : DbContext
{
    public DbSet<CpuMetric> CpuMetrics { get; set; }

    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CpuMetric>().HasKey(m => m.Id);
        modelBuilder.Entity<CpuMetric>().HasIndex(m => m.Timestamp); 
    }
}

public class CpuMetric
{
    public int Id { get; set; }
    public DateTime Timestamp { get; set; }
    public double Load { get; set; }
}
