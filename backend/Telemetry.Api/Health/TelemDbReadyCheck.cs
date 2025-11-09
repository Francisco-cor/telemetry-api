using Microsoft.Extensions.Diagnostics.HealthChecks;
using Telemetry.Api.Infra;

namespace Telemetry.Api.Health;

public sealed class TelemDbReadyCheck : IHealthCheck
{
    private readonly TelemDb _db;
    public TelemDbReadyCheck(TelemDb db) => _db = db;

    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var ok = await _db.Database.CanConnectAsync(cancellationToken);
            return ok ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("DB not reachable");
        }
        catch (Exception ex)
        {
            return HealthCheckResult.Unhealthy("DB check failed", ex);
        }
    }
}
