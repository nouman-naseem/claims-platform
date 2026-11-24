using Claims.Application.Exceptions;
using Claims.Domain.Common;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Claims.API.Infrastructure;

/// <summary>Single place where exceptions become consistent RFC 7807 problem responses.</summary>
public sealed class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _log;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> log) => _log = log;

    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken ct)
    {
        var (status, title, detail) = exception switch
        {
            NotFoundException e => (StatusCodes.Status404NotFound, "Resource not found", e.Message),
            ForbiddenException e => (StatusCodes.Status403Forbidden, "Forbidden", e.Message),
            AuthenticationFailedException e => (StatusCodes.Status401Unauthorized, "Authentication failed", e.Message),
            ConflictException e => (StatusCodes.Status409Conflict, "Conflict", e.Message),
            InvalidStateTransitionException e => (StatusCodes.Status409Conflict, "Invalid state transition", e.Message),
            DomainException e => (StatusCodes.Status422UnprocessableEntity, "Business rule violated", e.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "Concurrent modification",
                "The claim was modified by someone else. Reload and try again."),
            ValidationException => (StatusCodes.Status400BadRequest, "Validation failed", "One or more validation errors occurred."),
            _ => (StatusCodes.Status500InternalServerError, "Unexpected error", "An unexpected error occurred.")
        };

        if (status >= 500) _log.LogError(exception, "Unhandled exception on {Method} {Path}", context.Request.Method, context.Request.Path);
        else _log.LogInformation("Request rejected ({Status}): {Message}", status, exception.Message);

        var problem = new ProblemDetails { Status = status, Title = title, Detail = detail, Instance = context.Request.Path };
        problem.Extensions["traceId"] = context.TraceIdentifier;
        if (exception is ValidationException ve)
            problem.Extensions["errors"] = ve.Errors.GroupBy(f => f.PropertyName).ToDictionary(g => g.Key, g => g.Select(f => f.ErrorMessage).ToArray());

        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken: ct);
        return true;
    }
}
