using System.Globalization;
using Blocks.ServiceDefaults.Problems;
using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Infrastructure.Persistence;

namespace PdfBuilder.Api.Endpoints;

/// <summary>
/// Turns the failures the use cases throw into RFC 9457 problem responses, so endpoints describe
/// only the happy path. Anything not listed falls through to the default 500.
/// </summary>
internal sealed class ApiExceptionHandler(IProblemDetailsService problemDetails) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var problem = exception switch
        {
            ValidationException validation => new HttpValidationProblemDetails(
                validation.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).Distinct().ToArray()))
            {
                Status = StatusCodes.Status400BadRequest,
                Title = "One or more validation errors occurred."
            },
            ClientRequiredException => ClientRequiredProblem.Create(),
            TemplateNotFoundException e => Problem(StatusCodes.Status404NotFound, "Template not found", e),
            TemplateKeyTakenException e => Problem(StatusCodes.Status409Conflict, "Template key taken", e),
            TemplateReadOnlyException e => Problem(StatusCodes.Status409Conflict, "Template is read-only", e),
            StaleTemplateRevisionException e => Problem(StatusCodes.Status409Conflict, "Template changed", e),
            TemplateRenderException e => Problem(StatusCodes.Status422UnprocessableEntity, "Template does not render", e),
            JobHasNoFileException e => Problem(StatusCodes.Status409Conflict, "Job has no file", e),
            FileExpiredException e => Problem(StatusCodes.Status410Gone, "File expired", e),
            _ when StorageFailures.IsUnavailable(exception) => StorageUnavailable(),
            _ => null
        };

        if (problem is null)
            return false;

        if (problem.Status == StatusCodes.Status503ServiceUnavailable)
            httpContext.Response.Headers.RetryAfter = StorageRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    /// <summary>The message is safe to show: domain exceptions are written for the caller.</summary>
    /// <summary>How long a caller is told to wait before trying again when the database is unreachable.</summary>
    private const int StorageRetryAfterSeconds = 5;

    /// <summary>
    /// The detail is fixed on purpose: the provider's own message can name the host, the database
    /// or the user, and none of that belongs in a response.
    /// </summary>
    private static ProblemDetails StorageUnavailable() => new()
    {
        Status = StatusCodes.Status503ServiceUnavailable,
        Title = "Storage unavailable",
        Detail = "The service cannot reach its database right now. Nothing was accepted; try again in a few seconds.",
        Extensions = { ["code"] = StorageFailures.UnavailableCode }
    };

    private static ProblemDetails Problem(int status, string title, PdfBuilderException exception) => new()
    {
        Status = status,
        Title = title,
        Detail = exception.Message,
        Extensions = { ["code"] = exception.ErrorCode }
    };
}
