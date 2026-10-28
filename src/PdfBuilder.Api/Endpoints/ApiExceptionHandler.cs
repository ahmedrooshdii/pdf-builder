using FluentValidation;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using PdfBuilder.Domain.Exceptions;

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
            TemplateNotFoundException e => Problem(StatusCodes.Status404NotFound, "Template not found", e),
            TemplateKeyTakenException e => Problem(StatusCodes.Status409Conflict, "Template key taken", e),
            TemplateReadOnlyException e => Problem(StatusCodes.Status409Conflict, "Template is read-only", e),
            StaleTemplateRevisionException e => Problem(StatusCodes.Status409Conflict, "Template changed", e),
            TemplateRenderException e => Problem(StatusCodes.Status422UnprocessableEntity, "Template does not render", e),
            _ => null
        };

        if (problem is null)
            return false;

        httpContext.Response.StatusCode = problem.Status!.Value;
        return await problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception
        });
    }

    /// <summary>The message is safe to show: domain exceptions are written for the caller.</summary>
    private static ProblemDetails Problem(int status, string title, PdfBuilderException exception) => new()
    {
        Status = status,
        Title = title,
        Detail = exception.Message,
        Extensions = { ["code"] = exception.ErrorCode }
    };
}
