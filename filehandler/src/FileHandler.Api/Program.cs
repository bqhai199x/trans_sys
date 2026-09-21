using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using FileHandler.Api.Modules.PlainText;
using FileHandler.Api.Modules.Office;
using FileHandler.Api.Modules.Word;
using FileHandler.Api.Modules.Excel;
using FileHandler.Api.Modules.PowerPoint;
using FileHandler.Api.OpenApi;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<FileHandlingOptions>(builder.Configuration.GetSection(FileHandlingOptions.SectionName));
builder.Services.Configure<OfficeProcessingOptions>(builder.Configuration.GetSection(OfficeProcessingOptions.SectionName));

// Startup validation of options
var officeOpts = builder.Configuration.GetSection(OfficeProcessingOptions.SectionName).Get<OfficeProcessingOptions>() ?? new OfficeProcessingOptions();
officeOpts.Validate();

builder.Services.AddSingleton(MarkdownProfile.CreatePipeline());
builder.Services.AddSingleton<IMarkdownExtractor, MarkdownExtractor>();
builder.Services.AddSingleton<PlainTextService>();
builder.Services.AddSingleton(sp => new MarkdownService(sp.GetRequiredService<IOptions<FileHandlingOptions>>(), sp.GetRequiredService<IMarkdownExtractor>()));

// Office shared components
builder.Services.AddSingleton(sp => new OfficePackageReader(sp.GetRequiredService<IOptions<OfficeProcessingOptions>>().Value));
builder.Services.AddSingleton(sp => new OfficePackageInspector(sp.GetRequiredService<IOptions<OfficeProcessingOptions>>().Value));
builder.Services.AddSingleton(sp => new OfficeTextCodec(
    sp.GetRequiredService<IOptions<OfficeProcessingOptions>>().Value,
    sp.GetRequiredService<IOptions<FileHandlingOptions>>().Value));
builder.Services.AddSingleton(sp => new OfficePackageValidator(sp.GetRequiredService<IOptions<OfficeProcessingOptions>>().Value));

// Word components
builder.Services.AddSingleton<WordTableReader>();
builder.Services.AddSingleton<IWordExtractor>(sp => new WordExtractor(
    sp.GetRequiredService<OfficeTextCodec>(),
    sp.GetRequiredService<WordTableReader>(),
    sp.GetRequiredService<IOptions<OfficeProcessingOptions>>().Value));
builder.Services.AddSingleton<WordTranslationApplier>();
builder.Services.AddSingleton<WordStructureValidator>();
builder.Services.AddSingleton<WordService>();

// Excel components
builder.Services.AddSingleton<ExcelTableReader>();
builder.Services.AddSingleton<IExcelExtractor>(sp => new ExcelExtractor(
    sp.GetRequiredService<OfficeTextCodec>(),
    sp.GetRequiredService<ExcelTableReader>(),
    sp.GetRequiredService<IOptions<OfficeProcessingOptions>>().Value));
builder.Services.AddSingleton<ExcelTranslationApplier>();
builder.Services.AddSingleton<ExcelStructureValidator>();
builder.Services.AddSingleton<ExcelService>();

// PowerPoint components
builder.Services.AddSingleton<PowerPointTableReader>();
builder.Services.AddSingleton<IPowerPointExtractor>(sp => new PowerPointExtractor(
    sp.GetRequiredService<OfficeTextCodec>(),
    sp.GetRequiredService<PowerPointTableReader>(),
    sp.GetRequiredService<IOptions<OfficeProcessingOptions>>().Value));
builder.Services.AddSingleton<PowerPointTranslationApplier>();
builder.Services.AddSingleton<PowerPointStructureValidator>();
builder.Services.AddSingleton<PowerPointService>();
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
            return new ObjectResult(FileResponses.Failure(context.HttpContext, new[]
            {
                new FileError("file_too_large", ProcessingMessages.RequestTooLarge)
            }))
            {
                StatusCode = StatusCodes.Status413PayloadTooLarge
            };
        }

        return new BadRequestObjectResult(FileResponses.Failure(context.HttpContext, new[]
        {
            new FileError("invalid_request", ProcessingMessages.InvalidRequest)
        }));
    };
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.OperationFilter<ExportOperationFilter>();
    options.SchemaFilter<VietnameseSchemaFilter>();
});
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(x =>
{
    x.ValueLengthLimit = int.MaxValue;
    x.MultipartBodyLengthLimit = int.MaxValue;
    x.MultipartBoundaryLengthLimit = int.MaxValue;
    x.MultipartHeadersCountLimit = int.MaxValue;
    x.MultipartHeadersLengthLimit = int.MaxValue;
    x.ValueCountLimit = int.MaxValue;
    x.BufferBodyLengthLimit = int.MaxValue;
});

builder.Services.Configure<Microsoft.AspNetCore.Server.Kestrel.Core.KestrelServerOptions>(options =>
{
    options.Limits.MaxRequestBodySize = null;
});

builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = int.MaxValue;
    options.MaxRequestBodyBufferSize = int.MaxValue;
});

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("file-processing", context => System.Threading.RateLimiting.RateLimitPartition.GetConcurrencyLimiter(
        "file-processing", _ => new System.Threading.RateLimiting.ConcurrencyLimiterOptions
        {
            PermitLimit = Math.Clamp(context.RequestServices.GetRequiredService<IOptions<FileHandlingOptions>>().Value.MaxConcurrentRequests, 1, 64),
            QueueLimit = 0
        }));
    options.OnRejected = async (context, token) =>
    {
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsJsonAsync(FileResponses.Failure(context.HttpContext, [new("request_limit_exceeded", ProcessingMessages.RequestLimitExceeded)]), token);
    };
});

var app = builder.Build();
app.UseExceptionHandler();
app.Use(async (context, next) =>
{
    var path = (context.Request.Path.Value ?? string.Empty).TrimEnd('/');
    var isImportOrExport = (path.EndsWith("/import", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/export", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/sheets", StringComparison.OrdinalIgnoreCase) || path.EndsWith("/slides", StringComparison.OrdinalIgnoreCase)) &&
        context.Request.Method.Equals("POST", StringComparison.OrdinalIgnoreCase);
    if (isImportOrExport)
    {
        var limits = context.RequestServices.GetRequiredService<IOptions<FileHandlingOptions>>().Value;
        if (context.Request.ContentLength > limits.MaxMultipartBytes)
        {
            context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(FileResponses.Failure(context, new[]
            {
                new FileError("file_too_large", ProcessingMessages.MultipartSizeLimit(context.Request.ContentLength.Value, limits.MaxMultipartBytes))
            }), context.RequestAborted);
            return;
        }

        var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = limits.MaxMultipartBytes;
        context.Request.Body = new LimitedReadStream(context.Request.Body, limits.MaxMultipartBytes, "request_too_large");

        if (!context.Request.HasFormContentType || context.Request.ContentType?.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase) != true)
        {
            context.Response.StatusCode = StatusCodes.Status415UnsupportedMediaType;
            context.Response.ContentType = "application/json";
            await context.Response.WriteAsJsonAsync(FileResponses.Failure(context, new[]
            {
                new FileError("unsupported_media_type", ProcessingMessages.UnsupportedMediaType)
            }), context.RequestAborted);
            return;
        }
    }

    await next();
});
app.UseRouting();
app.UseRateLimiter();
app.UseStaticFiles();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "FileHandler API v1");
    options.DocumentTitle = "FileHandler API";
    options.InjectStylesheet("/swagger-custom.css");
    options.InjectJavascript("/swagger-multipart.js");
    options.InjectJavascript("/swagger-response.js");
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
        var requestTooLarge = exception is FileLimitException || exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
            || exception is InvalidDataException && exception.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase);
        if (!requestTooLarge) logger.LogError("Unhandled file handling failure of type {ExceptionType}; request {RequestId}.", exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = requestTooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status500InternalServerError;
        var error = requestTooLarge
            ? new FileError("request_too_large", ProcessingMessages.RequestTooLarge)
            : new FileError("internal_error", ProcessingMessages.InternalError);
        await context.Response.WriteAsJsonAsync(FileResponses.Failure(context, [error]), cancellationToken);
        return true;
    }
}
