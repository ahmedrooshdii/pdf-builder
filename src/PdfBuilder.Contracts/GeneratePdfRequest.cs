namespace PdfBuilder.Contracts;

/// <summary>
/// Ready-made <see cref="IGeneratePdfRequest"/>. Prefer it over an anonymous object: a misspelled
/// member is a compile error here, where an anonymous object silently sends the property as null.
/// </summary>
/// <remarks>
/// Send it through a request client or endpoint typed on <see cref="IGeneratePdfRequest"/>, so the
/// interface stays the message type on the wire. When publishing, name the interface explicitly:
/// <c>Publish&lt;IGeneratePdfRequest&gt;(new GeneratePdfRequest(...))</c>.
/// </remarks>
public sealed class GeneratePdfRequest : IGeneratePdfRequest
{
    /// <param name="correlationId">Your id for this job; see <see cref="IGeneratePdfRequest.CorrelationId"/>.</param>
    /// <param name="templateKey">Key of a published template; see <see cref="IGeneratePdfRequest.TemplateKey"/>.</param>
    /// <param name="jsonPayload">A JSON object as text; see <see cref="IGeneratePdfRequest.JsonPayload"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="templateKey"/> or <paramref name="jsonPayload"/> is null.</exception>
    public GeneratePdfRequest(Guid correlationId, string templateKey, string jsonPayload)
    {
        CorrelationId = correlationId;
        TemplateKey = templateKey ?? throw new ArgumentNullException(nameof(templateKey));
        JsonPayload = jsonPayload ?? throw new ArgumentNullException(nameof(jsonPayload));
    }

    /// <inheritdoc />
    public Guid CorrelationId { get; }

    /// <inheritdoc />
    public string TemplateKey { get; }

    /// <inheritdoc />
    public string JsonPayload { get; }

    /// <inheritdoc />
    public PdfPageSetup? PageSetup { get; set; }
}
