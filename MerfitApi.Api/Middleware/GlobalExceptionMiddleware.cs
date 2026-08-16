using System.Net;
using FluentValidation;
using Merfit.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Merfit.Api.Middleware;

/// <summary>
/// Single place that turns any unhandled exception into an RFC 7807 ProblemDetails response.
/// Business-rule/domain exceptions map to specific, meaningful status codes; anything else is a
/// 500 with no stack trace leaked outside Development.
/// </summary>
public sealed class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var (statusCode, title) = Map(exception);

        if (statusCode == HttpStatusCode.InternalServerError)
        {
            logger.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
        }
        else
        {
            logger.LogWarning(exception, "{Title} on {Method} {Path}", title, context.Request.Method, context.Request.Path);
        }

        var problemDetails = new ProblemDetails
        {
            Status = (int)statusCode,
            Title = title,
            Detail = exception is ValidationException
                ? "One or more validation errors occurred."
                : exception.Message,
            Instance = context.Request.Path,
        };

        if (exception is ValidationException validationException)
        {
            problemDetails.Extensions["errors"] = validationException.Errors
                .GroupBy(e => e.PropertyName)
                .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
        }

        if (environment.IsDevelopment())
        {
            problemDetails.Extensions["exception"] = exception.GetType().Name;
            problemDetails.Extensions["stackTrace"] = exception.StackTrace;
        }

        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)statusCode;
        await context.Response.WriteAsJsonAsync(problemDetails);
    }

    private static (HttpStatusCode StatusCode, string Title) Map(Exception exception) => exception switch
    {
        ValidationException => (HttpStatusCode.BadRequest, "Validation failed"),
        EntityNotFoundException => (HttpStatusCode.NotFound, "Resource not found"),
        OwnershipViolationException => (HttpStatusCode.Forbidden, "Forbidden"),
        BusinessRuleViolationException => (HttpStatusCode.UnprocessableEntity, "Business rule violation"),
        UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Unauthorized"),
        _ => (HttpStatusCode.InternalServerError, "An unexpected error occurred"),
    };
}

public static class GlobalExceptionMiddlewareExtensions
{
    public static IApplicationBuilder UseGlobalExceptionHandling(this IApplicationBuilder app) =>
        app.UseMiddleware<GlobalExceptionMiddleware>();
}
