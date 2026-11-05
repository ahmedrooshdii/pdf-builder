using MassTransit;
using MassTransit.Testing;
using Microsoft.Extensions.DependencyInjection;
using PdfBuilder.Contracts;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// <see cref="GeneratePdfRequest"/> is a concrete class, and MassTransit publishes a concrete type
/// under every interface it implements. These prove a consumer of the interface still sees each
/// request exactly once, with its values intact, however the caller sends it.
/// </summary>
public sealed class GeneratePdfRequestDeliveryTests
{
    private sealed class CountingConsumer : IConsumer<IGeneratePdfRequest>
    {
        public Task Consume(ConsumeContext<IGeneratePdfRequest> context) =>
            context.RespondAsync<IGeneratePdfResponse>(new
            {
                context.Message.CorrelationId,
                IsSuccess = true,
                CompletedAt = DateTimeOffset.UtcNow
            });
    }

    private static async Task<(ServiceProvider Provider, ITestHarness Harness)> StartAsync()
    {
        var provider = new ServiceCollection()
            .AddMassTransitTestHarness(x => x.AddConsumer<CountingConsumer>())
            .BuildServiceProvider(true);

        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        return (provider, harness);
    }

    [Fact]
    public async Task Publishing_the_class_without_a_type_argument_is_consumed_once()
    {
        var (provider, harness) = await StartAsync();
        await using var _ = provider;
        var request = new GeneratePdfRequest(NewId.NextGuid(), "invoice", "{}");

        await harness.Bus.Publish(request, TestContext.Current.CancellationToken);
        await harness.InactivityTask;

        var consumed = harness.GetConsumerHarness<CountingConsumer>().Consumed
            .Select<IGeneratePdfRequest>(TestContext.Current.CancellationToken)
            .ToList();
        var single = Assert.Single(consumed);
        Assert.Equal(request.CorrelationId, single.Context.Message.CorrelationId);
        Assert.Equal("invoice", single.Context.Message.TemplateKey);
    }

    [Fact]
    public async Task Publishing_as_the_interface_is_consumed_once()
    {
        var (provider, harness) = await StartAsync();
        await using var _ = provider;

        await harness.Bus.Publish<IGeneratePdfRequest>(
            new GeneratePdfRequest(NewId.NextGuid(), "invoice", "{}"), TestContext.Current.CancellationToken);
        await harness.InactivityTask;

        Assert.Single(harness.GetConsumerHarness<CountingConsumer>().Consumed.Select<IGeneratePdfRequest>(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Request_client_round_trips_the_class()
    {
        var (provider, harness) = await StartAsync();
        await using var _ = provider;
        var request = new GeneratePdfRequest(NewId.NextGuid(), "invoice", "{}");

        var client = harness.GetRequestClient<IGeneratePdfRequest>();
        var response = await client.GetResponse<IGeneratePdfResponse>(request, TestContext.Current.CancellationToken);

        Assert.Equal(request.CorrelationId, response.Message.CorrelationId);
        Assert.Single(harness.GetConsumerHarness<CountingConsumer>().Consumed.Select<IGeneratePdfRequest>(TestContext.Current.CancellationToken));
    }

    private static async Task<IGeneratePdfRequest> ConsumedAsync(ITestHarness harness)
    {
        await harness.InactivityTask;
        var consumed = harness.GetConsumerHarness<CountingConsumer>().Consumed
            .Select<IGeneratePdfRequest>(TestContext.Current.CancellationToken)
            .ToList();
        return Assert.Single(consumed).Context.Message;
    }

    [Fact]
    public async Task A_request_from_a_publisher_that_predates_the_client_id_arrives_with_none()
    {
        // The shape a 1.0.0 or 1.1.0 publisher sends: no ClientId at all. The change to the message is
        // additive, so it is still delivered, and the service reads the missing member as "the default client".
        var (provider, harness) = await StartAsync();
        await using var _ = provider;

        await harness.Bus.Publish<IGeneratePdfRequest>(new
        {
            CorrelationId = NewId.NextGuid(),
            TemplateKey = "invoice",
            JsonPayload = "{}",
            PageSetup = (PdfPageSetup?)null
        }, TestContext.Current.CancellationToken);

        Assert.Null((await ConsumedAsync(harness)).ClientId);
    }

    [Fact]
    public async Task A_client_id_set_on_the_ready_made_class_reaches_the_consumer()
    {
        var (provider, harness) = await StartAsync();
        await using var _ = provider;

        await harness.Bus.Publish<IGeneratePdfRequest>(
            new GeneratePdfRequest(NewId.NextGuid(), "invoice", "{}") { ClientId = "erp" }, TestContext.Current.CancellationToken);

        Assert.Equal("erp", (await ConsumedAsync(harness)).ClientId);
    }
}
