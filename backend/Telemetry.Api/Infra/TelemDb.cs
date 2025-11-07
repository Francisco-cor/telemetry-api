using Microsoft.EntityFrameworkCore;
using Telemetry.Api.Domain;

namespace Telemetry.Api.Infra;

public class TelemDb : DbContext
{
    public TelemDb(DbContextOptions<TelemDb> options) : base(options) { }
    public DbSet<TelemetryEvent> Telemetry => Set<TelemetryEvent>();

    protected override void OnModelCreating(ModelBuilder mb)
    {
        var e = mb.Entity<TelemetryEvent>();
        e.HasKey(t => t.Id);
        e.Property(t => t.Source).HasMaxLength(100).IsRequired();
        e.Property(t => t.MetricName).HasMaxLength(100).IsRequired();
        e.HasIndex(t => new { t.Source, t.Timestamp })
         .HasDatabaseName("IX_Telemetry_Source_Ts");
    }
}
