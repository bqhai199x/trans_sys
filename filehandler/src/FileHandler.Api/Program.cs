using FileHandler.Api.Common;
using FileHandler.Api.Diagnostics;
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
builder.Services.Configure<DebugTraceOptions>(builder.Configuration.GetSection(DebugTraceOptions.SectionName));
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

        var bodyLimit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
        if (bodyLimit is { IsReadOnly: false }) bodyLimit.MaxRequestBodySize = limits.MaxMultipartBytes;
        context.Request.Body = new LimitedReadStream(context.Request.Body, limits.MaxMultipartBytes, "request_too_large");

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
        var requestTooLarge = exception is FileLimitException || exception is BadHttpRequestException { StatusCode: StatusCodes.Status413PayloadTooLarge }
            || exception is InvalidDataException && exception.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase);
        if (!requestTooLarge) logger.LogError("Unhandled file handling failure of type {ExceptionType}; request {RequestId}.", exception.GetType().Name, context.TraceIdentifier);
        context.Response.StatusCode = requestTooLarge ? StatusCodes.Status413PayloadTooLarge : StatusCodes.Status500InternalServerError;
        var error = requestTooLarge
            ? new FileError("request_too_large", "Multipart request vượt giới hạn cho phép.")
            : new FileError("internal_error", "Đã xảy ra lỗi hệ thống.");
        await context.Response.WriteAsJsonAsync(new[] { error }, cancellationToken);
        return true;
    }
}
