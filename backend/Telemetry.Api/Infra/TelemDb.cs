using Microsoft.EntityFrameworkCore;
using Telemetry.Api.Domain;

namespace Telemetry.Api.Infra;

public class TelemDb : DbContext
{
    public TelemDb(DbContextOptions<TelemDb> options) : base(options) { }

    public DbSet<TelemetryEvent> Telemetry => Set<TelemetryEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<TelemetryEvent>();
        entity.ToTable("TelemetryEvent");
        entity.HasKey(e => e.Id);
        entity.Property(e => e.Source).HasMaxLength(100).IsRequired();
        entity.Property(e => e.MetricName).HasMaxLength(100).IsRequired();
        entity.HasIndex(e => new { e.Source, e.Timestamp })
              .HasDatabaseName("IX_Telemetry_Source_Timestamp");
    }
}