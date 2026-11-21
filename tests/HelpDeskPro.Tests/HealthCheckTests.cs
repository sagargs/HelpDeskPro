using System.Text.Json;
using HelpDeskPro.API.Data;
using HelpDeskPro.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDeskPro.Tests;

public class HealthCheckTests
{
    [Fact]
    public async Task DatabaseHealthCheckReportsAvailableDatabase()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        using var provider = services.BuildServiceProvider();
        var check = new DatabaseHealthCheck(
            provider.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<DatabaseHealthCheck>.Instance);

        var result = await check.CheckHealthAsync(new HealthCheckContext());

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task HealthResponseDoesNotExposeCheckExceptionDetails()
    {
        const string privateDetail = "server=internal;password=do-not-return-this";
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["sqlserver"] = new HealthReportEntry(
                    HealthStatus.Unhealthy,
                    "Database unavailable",
                    TimeSpan.Zero,
                    new InvalidOperationException(privateDetail),
                    null)
            },
            TimeSpan.Zero);
        var context = new DefaultHttpContext();
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;

        await HealthCheckResponseWriter.WriteAsync(context, report);

        Assert.StartsWith("application/json", context.Response.ContentType);
        responseBody.Position = 0;
        using var document = await JsonDocument.ParseAsync(responseBody);
        Assert.Equal("unhealthy", document.RootElement.GetProperty("status").GetString());
        Assert.DoesNotContain(privateDetail, document.RootElement.GetRawText());
    }
}
