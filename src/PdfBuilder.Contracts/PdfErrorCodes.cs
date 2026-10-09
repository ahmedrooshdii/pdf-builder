namespace PdfBuilder.Contracts;

/// <summary>
/// Machine-readable values of <see cref="IGeneratePdfResponse.ErrorCode"/>.
/// Callers branch on these instead of parsing <see cref="IGeneratePdfResponse.ErrorMessage"/>,
/// which is for humans and may change wording at any time.
/// </summary>
public static class PdfErrorCodes
{
    /// <summary>The request did not satisfy the contract (empty template id, payload is not a JSON object, …).</summary>
    public const string ValidationFailed = "VALIDATION_FAILED";

    /// <summary>No template is registered under the requested id.</summary>
    public const string TemplateNotFound = "TEMPLATE_NOT_FOUND";

    /// <summary>The template could not be bound to the payload: syntax error, or a runtime error while rendering.</summary>
    public const string TemplateRenderFailed = "TEMPLATE_RENDER_FAILED";

    /// <summary>The browser failed to produce a PDF. Transient; the message is retried before this is reported.</summary>
    public const string RenderFailed = "RENDER_FAILED";
}
