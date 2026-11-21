using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace HelpDeskPro.API.Middleware;

public static class HealthCheckResponseWriter
{
    public static Task WriteAsync(HttpContext context, HealthReport report)
    {
        context.Response.ContentType = "application/json";
        return context.Response.WriteAsJsonAsync(
            new { status = report.Status.ToString().ToLowerInvariant() },
            context.RequestAborted);
    }
}
