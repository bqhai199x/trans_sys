using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;
using FileHandler.Api.Modules.Markdown;
using FileHandler.Api.Modules.PlainText;
using FileHandler.Api.OpenApi;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<DebugTraceOptions>(builder.Configuration.GetSection(DebugTraceOptions.SectionName));
builder.Services.Configure<FileHandlingOptions>(builder.Configuration.GetSection(FileHandlingOptions.SectionName));
builder.Services.AddSingleton(MarkdownProfile.CreatePipeline());
builder.Services.AddSingleton<IMarkdownExtractor, MarkdownExtractor>();
builder.Services.AddSingleton<PlainTextService>();
builder.Services.AddSingleton(sp => new MarkdownService(sp.GetRequiredService<IOptions<FileHandlingOptions>>(), sp.GetRequiredService<IMarkdownExtractor>()));
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var isLimitError = context.ModelState.Values.Any(v => v.Errors.Any(e =>
            (e.Exception is InvalidDataException ide && ide.Message.Contains("limit", StringComparison.OrdinalIgnoreCase))
            || e.Exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
            || (e.ErrorMessage?.Contains("limit", StringComparison.OrdinalIgnoreCase) ?? false)
            || (e.ErrorMessage?.Contains("too large", StringComparison.OrdinalIgnoreCase) ?? false)));

        if (isLimitError)
        {
            return new ObjectResult(new[]
            {
                new FileError("file_too_large", "Kích thước multipart request vượt quá giới hạn cho phép.")
            })
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge
            };
        }

        return new BadRequestObjectResult(new[]
        {
            new FileError("invalid_request", "Multipart request không hợp lệ.")
        });
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<ExportOperationFilter>();
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOptions<FormOptions>().Configure<IOptions<FileHandlingOptions>>((form, configured) =>
    form.MultipartBodyLengthLimit = configured.Value.MaxMultipartBytes);

var app = builder.Build();
app.UseMiddleware<DebugTraceMiddleware>();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if ((path.Equals("/import", StringComparison.OrdinalIgnoreCase) || path.Equals("/export", StringComparison.OrdinalIgnoreCase)) &&
        context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<FileHandlingOptions>>().Value;
        if (context.Request.ContentLength > limits.MaxMultipartBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new[]
            {
                new FileError("file_too_large", $"Kích thước multipart request ({context.Request.ContentLength.Value} bytes) vượt quá giới hạn cho phép ({limits.MaxMultipartBytes} bytes).")
            });
            return;
        }

        if (!context.Request.HasFormContentType)
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(new[]
            {
                new FileError("unsupported_media_type", "Content-Type phải là multipart/form-data.")
            });
            return;
        }
    }

    await next();
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "FileHandler API v1");
    options.DocumentTitle = "FileHandler API";
    options.InjectStylesheet("/swagger-custom.css");
    options.InjectJavascript("/swagger-custom.js");
});
app.MapControllers();
app.Run();

/// <summary>
/// Application entry point and hosting pipeline configuration.
/// </summary>
public partial class Program;

/// <summary>
/// Creates handler for unhandled request failures.
/// </summary>
/// <param name="logger">Logger for unexpected failures.</param>
internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{

    /// <summary>
    /// Writes JSON error response for oversized requests or unhandled failures.
    /// </summary>
    /// <param name="context">Current HTTP request context.</param>
    /// <param name="exception">Unhandled request failure.</param>
    /// <param name="cancellationToken">Token for cancelling this operation.</param>
    /// <returns>True after writing error response.</returns>
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        var requestTooLarge = exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
            || exception is InvalidDataException && exception.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase);
        if (!requestTooLarge) logger.LogError(exception, "Unhandled file handling failure (request content omitted).");
        context.Response.StatusCode = requestTooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status500InternalServerError;
        var error = requestTooLarge
            ? new FileError("request_too_large", "Multipart request vượt giới hạn cho phép.")
            : new FileError("internal_error", "Đã xảy ra lỗi hệ thống.");
        await context.Response.WriteAsJsonAsync(new[] { error }, cancellationToken);
        return true;
    }
}
