using System.ComponentModel.DataAnnotations;

namespace PdfBuilder.Api.Messaging;

/// <summary>
/// Broker connection settings, in either of two shapes.
/// </summary>
/// <remarks>
/// Hosted brokers (CloudAMQP, AmazonMQ, …) hand out a single URI that already carries the
/// credentials, the virtual host and the TLS scheme, so <see cref="ConnectionString"/> takes it
/// verbatim and wins when it is set. A local or in-cluster broker is easier to express as the
/// discrete fields, which is what the defaults cover.
/// </remarks>
public sealed class RabbitMqOptions : IValidatableObject
{
    public const string Section = "RabbitMq";

    /// <summary>
    /// Full broker URI, for example <c>amqps://user:pass@host.rmq.cloudamqp.com/vhost</c>.
    /// The <c>amqps</c> scheme turns on TLS and moves the default port to 5671.
    /// Contains a password, so supply it through user secrets or the environment - never a
    /// committed settings file.
    /// </summary>
    public string? ConnectionString { get; init; }

    /// <summary>Ignored when <see cref="ConnectionString"/> is set.</summary>
    [Required]
    public string Host { get; init; } = "localhost";

    /// <summary>Ignored when <see cref="ConnectionString"/> is set.</summary>
    [Required]
    public string VirtualHost { get; init; } = "/";

    /// <summary>Ignored when <see cref="ConnectionString"/> is set.</summary>
    [Required]
    public string Username { get; init; } = "guest";

    /// <summary>Ignored when <see cref="ConnectionString"/> is set.</summary>
    [Required]
    public string Password { get; init; } = "guest";

    /// <summary>True when the broker is addressed by URI rather than by the discrete fields.</summary>
    public bool UsesConnectionString => !string.IsNullOrWhiteSpace(ConnectionString);

    /// <summary>
    /// Checked at startup, because a malformed broker URI is otherwise discovered as a connection
    /// loop in the logs rather than as a failed deployment.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!UsesConnectionString)
            yield break;

        if (!Uri.TryCreate(ConnectionString, UriKind.Absolute, out var uri))
        {
            yield return new ValidationResult(
                $"RabbitMq:ConnectionString is not an absolute URI.", [nameof(ConnectionString)]);
            yield break;
        }

        if (uri.Scheme is not ("amqp" or "amqps"))
        {
            yield return new ValidationResult(
                $"RabbitMq:ConnectionString must use the amqp or amqps scheme, not '{uri.Scheme}'.",
                [nameof(ConnectionString)]);
        }
    }
}
