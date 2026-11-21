using System.Text.Json;
using HelpDeskPro.API.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace HelpDeskPro.Tests;

public class GlobalExceptionHandlerTests
{
    [Fact]
    public async Task UnexpectedExceptionReturnsSafeProblemDetails()
    {
        const string privateDetail = "SQL connection password=do-not-return-this";
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/dashboard/summary";
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(
            context,
            new InvalidOperationException(privateDetail),
            CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);

        responseBody.Position = 0;
        using var document = await JsonDocument.ParseAsync(responseBody);
        var problem = document.RootElement;
        Assert.Equal(StatusCodes.Status500InternalServerError, problem.GetProperty("status").GetInt32());
        Assert.Equal("An unexpected error occurred.", problem.GetProperty("title").GetString());
        Assert.Equal("The request could not be completed.", problem.GetProperty("detail").GetString());
        Assert.Equal("/api/dashboard/summary", problem.GetProperty("instance").GetString());
        Assert.DoesNotContain(privateDetail, problem.GetRawText());
        Assert.DoesNotContain("stackTrace", problem.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RequestCancellationIsHandledWithoutGeneratingErrorResponse()
    {
        var context = new DefaultHttpContext
        {
            RequestAborted = new CancellationToken(canceled: true)
        };
        await using var responseBody = new MemoryStream();
        context.Response.Body = responseBody;
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance);

        var handled = await handler.TryHandleAsync(
            context,
            new OperationCanceledException(),
            context.RequestAborted);

        Assert.True(handled);
        Assert.Empty(responseBody.ToArray());
    }
}
