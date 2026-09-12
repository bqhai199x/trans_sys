using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace FileHandler.Tests.Api;

/// <summary>
/// Unit tests for global exception handler error response mapping.
/// </summary>
public sealed class GlobalExceptionHandlerTests
{

    /// <summary>
    /// Verifies error responses omit exception details.
    /// </summary>
    /// <param name="kind">Exception scenario to test.</param>
    /// <param name="status">Expected HTTP status code.</param>
    /// <param name="code">Machine-readable error code.</param>
    /// <returns>Task representing test completion.</returns>
    [Theory]
    [InlineData("http", 413, "request_too_large")]
    [InlineData("multipart", 413, "request_too_large")]
    [InlineData("other", 500, "internal_error")]
    [InlineData("invalid-data", 500, "internal_error")]
    public async Task TryHandleAsync_MapsErrorsWithoutLeakingExceptionDetails(string kind, int status, string code)
    {
        using var services = new ServiceCollection().AddLogging().AddOptions().BuildServiceProvider();
        using var body = new MemoryStream();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Response.Body = body;
        Exception exception = kind switch
        {
            "http" => new BadHttpRequestException("secret", 413),
            "multipart" => new InvalidDataException("secret LENGTH LIMIT exceeded"),
            "invalid-data" => new InvalidDataException("secret"),
            _ => new InvalidOperationException("secret")
        };
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        Assert.True(await handler.TryHandleAsync(context, exception, default));
        Assert.Equal(status, context.Response.StatusCode);
        Assert.StartsWith("application/json", context.Response.ContentType);
        body.Position = 0;
        using var json = await JsonDocument.ParseAsync(body);
        Assert.Equal(code, json.RootElement[0].GetProperty("code").GetString());
        Assert.DoesNotContain("secret", json.RootElement.GetRawText());
    }
}
