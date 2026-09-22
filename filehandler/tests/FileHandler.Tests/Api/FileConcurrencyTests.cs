using FileHandler.Tests.Modules.Office;
using System.Text.Json;
using System.Net;
using System.Text;
using FileHandler.Api.Common;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace FileHandler.Tests.Api;

/// <summary>
/// Exercises request concurrency admission before multipart model binding.
/// </summary>
public sealed class FileConcurrencyTests
{

    /// <summary>
    /// Rejects excess requests without buffering them and releases permits after completion.
    /// </summary>
    /// <param name="route">Route competing for shared concurrency permit.</param>
    /// <returns>Task completing after rejection and recovery assertions.</returns>
    [Theory]
    [InlineData("/api/plaintext/import")]
    [InlineData("/api/plaintext/export")]
    [InlineData("/api/excel/sheets")]
    [InlineData("/api/powerpoint/slides")]
    public async Task Processing_RejectsExcessConcurrency_AndReleasesPermit(string route)
    {
        using var factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder => builder.ConfigureServices(services =>
            services.PostConfigure<FileHandlingOptions>(options => options.MaxConcurrentRequests = 1)));
        using var client = factory.CreateClient();
        var payload = Encoding.UTF8.GetBytes("--gate\r\nContent-Disposition: form-data; name=\"file\"; filename=\"a.txt\"\r\n\r\nHello\r\n--gate--\r\n");
        using var body = new GatedBody(payload);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = factory.Server.SendAsync(context =>
        {
            context.Response.OnCompleted(() =>
            {
                completed.TrySetResult();
                return Task.CompletedTask;
            });
            context.Request.Method = "POST";
            context.Request.Path = "/api/plaintext/import";
            context.Request.ContentType = "multipart/form-data; boundary=gate";
            context.Request.Body = body;
        });
        try
        {
            await body.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
            using var blocked = await Send(client, route);
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
            using var rejected = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
            Assert.Equal("request_limit_exceeded", rejected.RootElement.GetProperty("errors")[0].GetProperty("code").GetString());
            Assert.Equal("failed", rejected.RootElement.GetProperty("metadata").GetProperty("status").GetString());
        }
        finally
        {
            body.Release.TrySetResult();
            var response = await first.WaitAsync(TimeSpan.FromSeconds(10));
            await response.Response.Body.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken);
            await completed.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        using var recovered = await Send(client, route);
        Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    /// <summary>
    /// Sends a small text import.
    /// </summary>
    /// <param name="client">Test HTTP client.</param>
    /// <param name="route">Processing endpoint.</param>
    /// <returns>Response owned by caller.</returns>
    private static async Task<HttpResponseMessage> Send(HttpClient client, string route)
    {
        using var form = new MultipartFormDataContent();
        if (route.Contains("excel", StringComparison.Ordinal))
            form.Add(new ByteArrayContent(OfficeFixtureFactory.CreateSelectionWorkbook()), "file", "a.xlsx");
        else if (route.Contains("powerpoint", StringComparison.Ordinal))
            form.Add(new ByteArrayContent(OfficeFixtureFactory.CreateSelectionPresentation()), "file", "a.pptx");
        else form.Add(new StringContent("Hello"), "file", "a.txt");
        if (route.EndsWith("export", StringComparison.Ordinal)) form.Add(new StringContent("[\"Hello\"]"), "texts");
        return await client.PostAsync(route, form, TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Pauses model-binding reads while retaining an admission permit.
    /// </summary>
    private sealed class GatedBody : MemoryStream
    {

        /// <summary>
        /// Signals entry into request body reading.
        /// </summary>
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Releases body reading.
        /// </summary>
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Creates a bounded body stream.
        /// </summary>
        /// <param name="bytes">Multipart fixture.</param>
        internal GatedBody(byte[] bytes) : base(bytes) { }

        /// <summary>
        /// Waits for explicit release before reading fixture bytes.
        /// </summary>
        /// <param name="buffer">Destination memory.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Number of copied bytes.</returns>
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            return await base.ReadAsync(buffer, cancellationToken);
        }

        /// <summary>
        /// Routes legacy asynchronous reads through admission synchronization.
        /// </summary>
        /// <param name="buffer">Destination buffer.</param>
        /// <param name="offset">Destination offset.</param>
        /// <param name="count">Maximum bytes.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Number of copied bytes.</returns>
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
    }
}
