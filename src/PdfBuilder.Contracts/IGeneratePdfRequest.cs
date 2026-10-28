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

    /// <summary>Identifier of a registered HTML template, for example <c>invoice</c>.</summary>
    string TemplateKey { get; }

    /// <summary>A JSON object, serialised as text, that is bound to the template.</summary>
    string JsonPayload { get; }
}
