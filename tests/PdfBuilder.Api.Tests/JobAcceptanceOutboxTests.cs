using Blocks.ServiceDefaults.Identity;
using MassTransit;
using MassTransit.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Api.Tests.Support;
using PdfBuilder.Contracts;
using PdfBuilder.Domain.Jobs;
using PdfBuilder.Infrastructure.Jobs;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// FR-003: a job is accepted only if it is both recorded and queued. The endpoint publishes the
/// request and then saves the job; with the bus outbox those two writes are one transaction.
/// </summary>
public sealed class JobAcceptanceOutboxTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ServiceProvider BuildProvider(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddOptions<JobStoreOptions>();
        services.AddDbContext<PdfBuilderDbContext>(options => options.Configure(connectionString));
        services.AddScoped<IPdfJobStore, EfPdfJobStore>();

        // What the stud registers for a block: one caller per scope. Each scope below acts as alpha.
        services.AddScoped<FixedCurrentClient>();
        services.AddScoped<ICurrentClient>(provider => provider.GetRequiredService<FixedCurrentClient>());
        services.AddScoped<IClientContext>(provider => provider.GetRequiredService<FixedCurrentClient>());
        services.AddScoped<ClientScope>();

        // The same outbox configuration the service uses (MessagingExtensions.AddMessaging).
        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<ReceivedRequestProbe>();
            x.AddEntityFrameworkOutbox<PdfBuilderDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });
        });

        return services.BuildServiceProvider(validateScopes: true);
    }

    /// <summary>Stands in for the worker: it only has to receive the request.</summary>
    private sealed class ReceivedRequestProbe : IConsumer<IGeneratePdfRequest>
    {
        public Task Consume(ConsumeContext<IGeneratePdfRequest> context) => Task.CompletedTask;
    }

    private static Task PublishRequestAsync(IPublishEndpoint bus, Guid jobId) =>
        bus.Publish<IGeneratePdfRequest>(new
        {
            CorrelationId = jobId,
            TemplateKey = "invoice",
            JsonPayload = "{}",
            PageSetup = (PdfPageSetup?)null,
            ClientId = "alpha"
        }, Ct);

    /// <summary>Rows in <c>pdf_jobs</c> and <c>outbox_message</c>, read over a separate connection.</summary>
    private static async Task<(long Jobs, long Messages)> CountsAsync(TestDatabase database)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);

        await using var jobs = new NpgsqlCommand("SELECT count(*) FROM pdf_jobs", connection);
        await using var messages = new NpgsqlCommand("SELECT count(*) FROM outbox_message", connection);

        return ((long)(await jobs.ExecuteScalarAsync(Ct))!, (long)(await messages.ExecuteScalarAsync(Ct))!);
    }

    [Fact]
    public async Task Publishing_alone_writes_nothing_and_saving_commits_the_job_and_its_message_together()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha");
        await using var provider = BuildProvider(database.ConnectionString);
        var jobId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IClientContext>().ActAs("alpha");
            await PublishRequestAsync(scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(), jobId);

            // Queued in the context only: another connection sees neither a job nor a message yet.
            Assert.Equal((0, 0), await CountsAsync(database));

            await scope.ServiceProvider.GetRequiredService<IPdfJobStore>()
                .SaveAsync(PdfJob.Enqueued(jobId, "invoice", Now), Ct);
        }

        Assert.Equal((1, 1), await CountsAsync(database));
    }

    [Fact]
    public async Task When_recording_the_job_fails_no_message_is_left_behind()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha");
        await using var provider = BuildProvider(database.ConnectionString);
        var jobId = Guid.NewGuid();

        await using (var scope = provider.CreateAsyncScope())
        {
            scope.ServiceProvider.GetRequiredService<IClientContext>().ActAs("alpha");
            await PublishRequestAsync(scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(), jobId);

            // A key past the column's 64 characters: the database refuses the job row.
            var unsavable = PdfJob.Enqueued(jobId, new string('x', 65), Now);

            await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                scope.ServiceProvider.GetRequiredService<IPdfJobStore>().SaveAsync(unsavable, Ct));
        }

        Assert.Equal((0, 0), await CountsAsync(database));
    }

    [Fact]
    public async Task A_committed_message_is_delivered_to_the_bus()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha");
        await using var provider = BuildProvider(database.ConnectionString);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        // The harness starts the bus but not the hosted service that moves outbox rows onto it, which
        // the real host starts. Start that one by hand.
        var hosted = provider.GetServices<IHostedService>().Select(service => service.GetType().Name).ToList();
        var delivery = provider.GetServices<IHostedService>()
            .SingleOrDefault(service => service.GetType().Name.StartsWith("BusOutboxDeliveryService", StringComparison.Ordinal));
        Assert.True(delivery is not null, $"No outbox delivery service among: {string.Join(", ", hosted)}");
        await delivery.StartAsync(Ct);

        try
        {
            var jobId = Guid.NewGuid();

            await using (var scope = provider.CreateAsyncScope())
            {
                scope.ServiceProvider.GetRequiredService<IClientContext>().ActAs("alpha");
                await PublishRequestAsync(scope.ServiceProvider.GetRequiredService<IPublishEndpoint>(), jobId);
                await scope.ServiceProvider.GetRequiredService<IPdfJobStore>()
                    .SaveAsync(PdfJob.Enqueued(jobId, "invoice", Now), Ct);
            }

            Assert.True(await harness.Consumed.Any<IGeneratePdfRequest>(m => m.Context.Message.CorrelationId == jobId, Ct));
        }
        finally
        {
            await delivery.StopAsync(Ct);
        }
    }

    [Fact]
    public async Task A_job_submitted_over_http_is_published_with_its_client_and_recorded_under_it()
    {
        // The endpoint names the same owner in the message and in the job row, so the worker renders with that
        // client's templates and the job can be read by that client and nobody else (feature 003, R5.2).
        PostgresFixture.SkipUnlessEnabled();
        await using var api = await ApiFactory.StartAsync(postgres, services =>
            services.AddSingleton<PdfBuilder.Application.Abstractions.IPdfRenderer>(new PdfBuilder.Api.Tests.Support.RecordingRenderer()));
        using var beta = await api.ClientForAsync("beta", Ct);

        var jobId = await ApiFactory.SubmitJobAsync(beta, "invoice", new { }, Ct);
        await ApiFactory.WaitForJobAsync(beta, jobId, Ct);

        var harness = api.Services.GetRequiredService<ITestHarness>();
        Assert.True(await harness.Consumed.Any<IGeneratePdfRequest>(
            m => m.Context.Message.CorrelationId == jobId && m.Context.Message.ClientId == "beta", Ct));

        await using var connection = new NpgsqlConnection(api.Database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand("SELECT client_id FROM pdf_jobs WHERE job_id = @id", connection);
        command.Parameters.AddWithValue("id", jobId);
        Assert.Equal("beta", (string?)await command.ExecuteScalarAsync(Ct));
    }
}
