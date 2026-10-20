using System.Net;
using System.Text.Json;
using InvoiceTracker.Application.Common.Exceptions;

namespace InvoiceTracker.Api.Middleware;

/// <summary>
/// Translates Application-layer exceptions into clean HTTP status codes instead of letting them
/// surface as a bare 500 — e.g. a bad login must come back as 401/400, not an unhandled error.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        object payload;

        switch (exception)
        {
            case ValidationException validationException:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                payload = new { title = "Validation failed.", errors = validationException.Errors };
                break;

            case AuthenticationFailedException authenticationFailedException:
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                payload = new { title = authenticationFailedException.Message };
                break;

            case IdentityOperationException identityOperationException:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                payload = new { title = "Request could not be completed.", errors = identityOperationException.Errors };
                break;

            default:
                _logger.LogError(exception, "Unhandled exception");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                payload = new { title = "An unexpected error occurred." };
                break;
        }

        await context.Response.WriteAsync(JsonSerializer.Serialize(payload));
    }
}
