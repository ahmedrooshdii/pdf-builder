namespace PdfBuilder.Contracts;

/// <summary>
/// Machine-readable values of <see cref="IGeneratePdfResponse.ErrorCode"/>.
/// Callers branch on these instead of parsing <see cref="IGeneratePdfResponse.ErrorMessage"/>,
/// which is for humans and may change wording at any time.
/// </summary>
/// <remarks>
/// New codes may be added in a minor version, so a service newer than your copy of this package can
/// send one you do not know. Treat a code that fails <see cref="IsKnown"/> as a generic failure that
/// retrying will not fix.
/// </remarks>
public static class PdfErrorCodes
{
    /// <summary>
    /// The request broke a rule in <see cref="PdfRequestLimits"/>, the payload is not a JSON
    /// object, or <see cref="IGeneratePdfRequest.PageSetup"/> is malformed. Final: fix the request
    /// before sending it again.
    /// </summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>
    /// No published template exists under the requested key. A draft that was never published
    /// reports this too. Final: publish the template or fix the key.
    /// </summary>
    public const string TemplateNotFound = "TEMPLATE_NOT_FOUND";

    /// <summary>
    /// The template could not be bound to the payload: a syntax error, or a runtime error while
    /// rendering. Final: fix the template or the payload.
    /// </summary>
    public const string TemplateRenderFailed = "TEMPLATE_RENDER_FAILED";

    /// <summary>
    /// The browser failed to produce a PDF. The service treats this as transient and redelivers the
    /// message three times with backoff. If every attempt fails, the requester receives a
    /// <c>Fault&lt;IGeneratePdfRequest&gt;</c> (a <c>RequestFaultException</c> from a request client),
    /// not a response carrying this code. Handle the code anyway: if a response does carry it, it is
    /// final like the others.
    /// </summary>
    public const string RenderFailed = "RENDER_FAILED";

    /// <summary>Every code this version of the contracts knows. A newer service may send others.</summary>
    public static IReadOnlyList<string> All { get; } =
        Array.AsReadOnly(new[] { ValidationFailed, TemplateNotFound, TemplateRenderFailed, RenderFailed });

    /// <summary>True when <paramref name="errorCode"/> is one of <see cref="All"/>. Codes are case-sensitive.</summary>
    public static bool IsKnown(string? errorCode) => errorCode is not null && All.Contains(errorCode);
}
