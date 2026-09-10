using FileHandler.Api.Common;
using FileHandler.Api.Modules.Markdown;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Services.Configure<FileHandlingOptions>(builder.Configuration.GetSection(FileHandlingOptions.SectionName));
builder.Services.AddSingleton<MarkdownService>();
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = _ => new BadRequestObjectResult(new[]
    {
        new FileError("invalid_request", "Multipart request không hợp lệ.")
    });
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();
builder.Services.AddOptions<FormOptions>().Configure<IOptions<FileHandlingOptions>>((form, configured) =>
    form.MultipartBodyLengthLimit = configured.Value.MaxMultipartBytes);

var app = builder.Build();
app.UseExceptionHandler();
app.UseSwagger();
app.UseSwaggerUI(options =>
{
    options.SwaggerEndpoint("/swagger/v1/swagger.json", "FileHandler API v1");
    options.DocumentTitle = "FileHandler API";
});
app.MapControllers();
app.Run();

public partial class Program;

internal sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger) : Microsoft.AspNetCore.Diagnostics.IExceptionHandler
{
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
