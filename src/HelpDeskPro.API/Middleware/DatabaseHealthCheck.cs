using HelpDeskPro.API.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HelpDeskPro.API.Middleware;

public sealed class DatabaseHealthCheck(
    IServiceScopeFactory scopeFactory,
    ILogger<DatabaseHealthCheck> logger) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
        if (!canConnect)
        {
            logger.LogWarning("SQL Server readiness check failed.");
            return HealthCheckResult.Unhealthy("SQL Server is unavailable.");
        }

        return HealthCheckResult.Healthy();
    }
}
