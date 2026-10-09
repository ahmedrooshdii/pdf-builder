using System.ComponentModel.DataAnnotations;
using PdfBuilder.Api.Messaging;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// A broker URI is the one setting that is both easy to get wrong and expensive to debug, because
/// the failure otherwise arrives as a connection-retry loop with no mention of the setting.
/// </summary>
public sealed class RabbitMqOptionsTests
{
    private static string[] Validate(RabbitMqOptions options) =>
        [.. options.Validate(new ValidationContext(options)).Select(r => r.ErrorMessage!)];

    [Fact]
    public void Defaults_describe_a_local_broker_and_are_valid()
    {
        var options = new RabbitMqOptions();

        Assert.False(options.UsesConnectionString);
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData("amqp://guest:guest@localhost/")]
    [InlineData("amqps://user:pass@woodpecker.rmq.cloudamqp.com/user_vhost")]
    [InlineData("amqps://host.example.com:5671/vhost")]
    public void Accepts_amqp_and_amqps_uris(string uri)
    {
        var options = new RabbitMqOptions { ConnectionString = uri };

        Assert.True(options.UsesConnectionString);
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData("https://woodpecker.rmq.cloudamqp.com/vhost")]   // pasted the management URL
    [InlineData("rabbitmq://localhost")]                         // MassTransit's own scheme, not the broker's
    public void Rejects_a_uri_whose_scheme_is_not_amqp(string uri)
    {
        var errors = Validate(new RabbitMqOptions { ConnectionString = uri });

        Assert.Single(errors);
        Assert.Contains("amqp or amqps", errors[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not a uri")]
    [InlineData("woodpecker.rmq.cloudamqp.com/vhost")]   // host only, scheme forgotten
    public void Rejects_anything_that_is_not_an_absolute_uri(string value)
    {
        var errors = Validate(new RabbitMqOptions { ConnectionString = value });

        Assert.Single(errors);
        Assert.Contains("absolute URI", errors[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Treats_a_blank_connection_string_as_absent_rather_than_invalid(string? value)
    {
        var options = new RabbitMqOptions { ConnectionString = value };

        Assert.False(options.UsesConnectionString);
        Assert.Empty(Validate(options));
    }
}
