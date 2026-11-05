using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace Blocks.ServiceDefaults.Problems;

/// <summary>The stable <c>code</c> values the stud answers with. Public API: never renamed or repurposed.</summary>
public static class BlocksProblemCodes
{
    public const string AuthRequired = "AUTH_REQUIRED";
    public const string AuthInvalidKey = "AUTH_INVALID_KEY";
    public const string AuthScopeMissing = "AUTH_SCOPE_MISSING";
    public const string ClientDisabled = "CLIENT_DISABLED";
    public const string StorageUnavailable = "STORAGE_UNAVAILABLE";
    public const string ValidationFailed = "VALIDATION_FAILED";
    public const string ClientNotFound = "CLIENT_NOT_FOUND";
    public const string KeyNotFound = "KEY_NOT_FOUND";
    public const string ClientIdTaken = "CLIENT_ID_TAKEN";
    public const string ConsoleHeaderRequired = "CONSOLE_HEADER_REQUIRED";
    public const string RateLimited = "RATE_LIMITED";
}

/// <summary>
/// RFC 9457 problems for the stud's refusals. Every <c>title</c> and <c>detail</c> is fixed: nothing a
/// caller sent is ever repeated back, so a refusal cannot echo a key. The <c>traceId</c> is added by
/// the host's <see cref="IProblemDetailsService"/>.
/// </summary>
internal static class BlocksProblems
{
    /// <summary>How long a caller is told to wait when the database cannot be reached.</summary>
    public const int StorageRetryAfterSeconds = 5;

    public static ProblemDetails AuthRequired() => Create(
        StatusCodes.Status401Unauthorized,
        "Authentication required",
        $"Send an API key in the {Authentication.ApiKeyAuthenticationDefaults.HeaderName} header.",
        BlocksProblemCodes.AuthRequired);

    public static ProblemDetails InvalidKey() => Create(
        StatusCodes.Status401Unauthorized,
        "Invalid API key",
        "The API key is not valid. Check that it was copied whole and has not been revoked or expired.",
        BlocksProblemCodes.AuthInvalidKey);

    public static ProblemDetails ClientDisabled() => Create(
        StatusCodes.Status403Forbidden,
        "Client disabled",
        "The client this key belongs to is disabled. Ask the operator to enable it.",
        BlocksProblemCodes.ClientDisabled);

    public static ProblemDetails ScopeMissing() => Create(
        StatusCodes.Status403Forbidden,
        "Permission missing",
        "The API key is valid but does not have the permission this operation needs.",
        BlocksProblemCodes.AuthScopeMissing);

    public static ProblemDetails StorageUnavailable() => Create(
        StatusCodes.Status503ServiceUnavailable,
        "Storage unavailable",
        "The service cannot reach its database right now. Nothing was accepted; try again in a few seconds.",
        BlocksProblemCodes.StorageUnavailable);

    public static ProblemDetails Unexpected() => new()
    {
        Status = StatusCodes.Status500InternalServerError,
        Title = "An error occurred while processing your request."
    };

    public static ProblemDetails ClientRequired() => ClientRequiredProblem.Create();

    public static ProblemDetails ClientNotFound() => Create(
        StatusCodes.Status404NotFound, "Client not found", "No client has this id.", BlocksProblemCodes.ClientNotFound);

    public static ProblemDetails KeyNotFound() => Create(
        StatusCodes.Status404NotFound, "Key not found", "The client has no key with this id.", BlocksProblemCodes.KeyNotFound);

    public static ProblemDetails ClientIdTaken() => Create(
        StatusCodes.Status409Conflict,
        "Client id taken",
        "A client with this id already exists. Client ids are permanent and never reused.",
        BlocksProblemCodes.ClientIdTaken);

    public static ProblemDetails ConsoleHeaderRequired() => Create(
        StatusCodes.Status403Forbidden,
        "Console header required",
        $"Changes made with a console session must carry {Authentication.ConsoleSessionDefaults.HeaderName}: {Authentication.ConsoleSessionDefaults.HeaderValue}.",
        BlocksProblemCodes.ConsoleHeaderRequired);

    public static ProblemDetails RateLimited() => Create(
        StatusCodes.Status429TooManyRequests,
        "Too many attempts",
        "Too many failed sign-ins from this address. Try again later.",
        BlocksProblemCodes.RateLimited);

    public static HttpValidationProblemDetails Validation(IDictionary<string, string[]> errors) => new(errors)
    {
        Status = StatusCodes.Status400BadRequest,
        Title = "One or more validation errors occurred.",
        Extensions = { ["code"] = BlocksProblemCodes.ValidationFailed }
    };

    /// <summary>A problem as an endpoint result.</summary>
    public static IResult Result(ProblemDetails problem) =>
        problem is HttpValidationProblemDetails validation
            ? TypedResults.ValidationProblem(validation.Errors, extensions: validation.Extensions)
            : TypedResults.Problem(problem);

    /// <summary>Writes a problem outside an endpoint (from the authentication handler), with its headers.</summary>
    public static async Task WriteAsync(HttpContext context, ProblemDetails problem)
    {
        var response = context.Response;
        response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        switch (problem.Status)
        {
            case StatusCodes.Status401Unauthorized:
                response.Headers.WWWAuthenticate = Authentication.ApiKeyAuthenticationDefaults.Scheme;
                break;
            case StatusCodes.Status503ServiceUnavailable:
                response.Headers.RetryAfter = StorageRetryAfterSeconds.ToString(CultureInfo.InvariantCulture);
                break;
        }

        var writer = context.RequestServices.GetRequiredService<IProblemDetailsService>();
        await writer.WriteAsync(new ProblemDetailsContext { HttpContext = context, ProblemDetails = problem });
    }

    private static ProblemDetails Create(int status, string title, string detail, string code) => new()
    {
        Status = status,
        Title = title,
        Detail = detail,
        Extensions = { ["code"] = code }
    };
}
