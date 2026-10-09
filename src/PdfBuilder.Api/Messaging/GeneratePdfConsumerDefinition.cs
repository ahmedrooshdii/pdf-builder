using MassTransit;
using Microsoft.Extensions.Options;
using PdfBuilder.Infrastructure.Rendering;

namespace PdfBuilder.Api.Messaging;

public sealed class GeneratePdfConsumerDefinition : ConsumerDefinition<GeneratePdfConsumer>
{
    public GeneratePdfConsumerDefinition(IOptions<PlaywrightOptions> playwright)
    {
        // Matched to the renderer's own limit. Pulling more messages than we can render would only
        // move the queue from RabbitMQ into this process's memory, where it is invisible and where
        // a restart loses it.
        ConcurrentMessageLimit = playwright.Value.MaxConcurrentRenders;
    }

    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<GeneratePdfConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        // Only transient failures reach this policy; the consumer answers permanent ones itself.
        endpointConfigurator.UseMessageRetry(r => r.Exponential(
            retryLimit: 3,
            minInterval: TimeSpan.FromSeconds(1),
            maxInterval: TimeSpan.FromSeconds(30),
            intervalDelta: TimeSpan.FromSeconds(2)));
    }
}
