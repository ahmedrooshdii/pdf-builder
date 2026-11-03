using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PdfBuilder.Api.Messaging;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Application.Features.GeneratePdf;
using PdfBuilder.Contracts;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Jobs;
using PdfBuilder.Infrastructure.Jobs;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// The worker side with the real job store: a consumer runs in its own scope, so it must get its own
/// <c>DbContext</c> and write the outcome where an HTTP caller polling from any instance will read it.
/// </summary>
public sealed class GeneratePdfConsumerPersistenceTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed record GeneratePdfRequest(Guid CorrelationId, string TemplateKey, string JsonPayload) : IGeneratePdfRequest
    {
        public PdfPageSetup? PageSetup => null;
    }

    private static ServiceProvider BuildProvider(string connectionString, ISender sender)
    {
        var services = new ServiceCollection();
        services.AddSingleton(sender);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        services.AddSingleton(Options.Create(new JobStoreOptions()));
        services.AddDbContext<PdfBuilderDbContext>(options => options.Configure(connectionString));
        services.AddScoped<IPdfJobStore, EfPdfJobStore>();

        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<GeneratePdfConsumer>();
            x.AddEntityFrameworkOutbox<PdfBuilderDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });
        });

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static async Task<PdfJob?> ReadAsync(ServiceProvider provider, Guid jobId)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IPdfJobStore>().GetAsync(jobId, Ct);
    }

    private static async Task SaveAsync(ServiceProvider provider, PdfJob job)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPdfJobStore>().SaveAsync(job, Ct);
    }

    private static async Task ConsumeAsync(ServiceProvider provider, Guid jobId)
    {
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();
        await harness.Bus.Publish<IGeneratePdfRequest>(new GeneratePdfRequest(jobId, "invoice", "{}"), Ct);
        Assert.True(await harness.GetConsumerHarness<GeneratePdfConsumer>().Consumed.Any<IGeneratePdfRequest>(Ct));
    }

    [Fact]
    public async Task A_finished_render_is_recorded_as_succeeded_for_any_instance_to_read()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GeneratePdfCommand>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratePdfResult("a7e0495b2cd24ed9bb65b1b0f3a14c20", "http://x/files/a7e0495b2cd24ed9bb65b1b0f3a14c20", 2048));
        await using var provider = BuildProvider(database.ConnectionString, sender);
        var jobId = NewId.NextGuid();
        await SaveAsync(provider, PdfJob.Enqueued(jobId, "invoice", DateTimeOffset.UtcNow));

        await ConsumeAsync(provider, jobId);

        var job = await ReadAsync(provider, jobId);
        Assert.Equal(PdfJobStatus.Succeeded, job!.Status);
        Assert.Equal("a7e0495b2cd24ed9bb65b1b0f3a14c20", job.FileId);
        Assert.Equal(2048, job.SizeBytes);
    }

    [Fact]
    public async Task A_rejected_request_is_recorded_as_failed_with_its_code()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GeneratePdfCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TemplateNotFoundException("invoice"));
        await using var provider = BuildProvider(database.ConnectionString, sender);
        var jobId = NewId.NextGuid();
        await SaveAsync(provider, PdfJob.Enqueued(jobId, "invoice", DateTimeOffset.UtcNow));

        await ConsumeAsync(provider, jobId);

        var job = await ReadAsync(provider, jobId);
        Assert.Equal(PdfJobStatus.Failed, job!.Status);
        Assert.Equal(PdfErrorCodes.TemplateNotFound, job.ErrorCode);
    }

    [Fact]
    public async Task A_request_published_straight_to_the_bus_gets_a_job_record_when_it_completes()
    {
        // No HTTP endpoint was involved, so there is no pending row; the consumer creates the record.
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GeneratePdfCommand>(), Arg.Any<CancellationToken>())
            .Returns(new GeneratePdfResult("f1", "http://x/files/f1", 1));
        await using var provider = BuildProvider(database.ConnectionString, sender);
        var jobId = NewId.NextGuid();

        await ConsumeAsync(provider, jobId);

        Assert.Equal(PdfJobStatus.Succeeded, (await ReadAsync(provider, jobId))!.Status);
    }
}
