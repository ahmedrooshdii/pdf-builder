namespace PdfBuilder.Contracts;

/// <summary>Asks the PDF service to render <see cref="TemplateKey"/> bound to <see cref="JsonPayload"/>.</summary>
/// <remarks>
/// The namespace and type name form the MassTransit message URN, which becomes the RabbitMQ exchange name.
/// Never rename or move this type once it is published, or producers and consumers stop matching.
/// </remarks>
public interface IGeneratePdfRequest
{
    /// <summary>Caller-generated id used to correlate the response and to trace the job in logs.</summary>
    Guid CorrelationId { get; }

    /// <summary>
    /// Key of a published template, for example <c>invoice</c>. Must match
    /// <see cref="PdfRequestLimits.TemplateKeyPattern"/>. A draft that was never published is
    /// answered <see cref="PdfErrorCodes.TemplateNotFound"/>.
    /// </summary>
    string TemplateKey { get; }

    /// <summary>
    /// A JSON object, serialised as text, that is bound to the template. At most
    /// <see cref="PdfRequestLimits.MaxPayloadBytes"/> in UTF-8.
    /// </summary>
    string JsonPayload { get; }

    /// <summary>
    /// Optional: paper size, orientation or margins for this document only. Null renders with the
    /// template's own page setup, which is also what a publisher built before this member existed gets.
    /// </summary>
    PdfPageSetup? PageSetup { get; }

    /// <summary>
    /// The client this job is for, for example <c>ecommerce</c>. Null means the deployment's default
    /// client (<c>Blocks:Bus:DefaultClientId</c>), which is also what a publisher built before this
    /// member existed gets. Must match <c>^[a-z][a-z0-9-]{0,39}$</c>. The service cannot check that the
    /// message really comes from this client: give broker credentials only to trusted product backends.
    /// </summary>
    string? ClientId { get; }
}
