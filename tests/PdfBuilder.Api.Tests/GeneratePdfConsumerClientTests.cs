using Blocks.ServiceDefaults;
using Blocks.ServiceDefaults.EntityFramework;
using Blocks.ServiceDefaults.Identity;
using Blocks.ServiceDefaults.Tests.Support;
using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
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
/// Feature 003, User Story 3: the table in contracts/message-contract.md, one test per row. The real
/// consumer, the real job store and a real database; only the rendering is a stand-in.
/// </summary>
public sealed class GeneratePdfConsumerClientTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class Rig(ServiceProvider provider, TestDatabase database, ISender sender, CapturingLoggerProvider logs) : IAsyncDisposable
    {
        public ServiceProvider Provider { get; } = provider;
        public TestDatabase Database { get; } = database;
        public ISender Sender { get; } = sender;
        public CapturingLoggerProvider Logs { get; } = logs;

        public async ValueTask DisposeAsync() => await Provider.DisposeAsync();
    }

    /// <summary>
    /// A deployment with clients <c>alpha</c>, <c>beta</c> and a disabled <c>gamma</c>, whose default client is
    /// <paramref name="defaultClientId"/>, and a renderer that succeeds unless <paramref name="renderFails"/>.
    /// </summary>
    private async Task<Rig> StartAsync(string? defaultClientId, bool renderFails = false)
    {
        PostgresFixture.SkipUnlessEnabled();
        var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta", "gamma");
        await ExecuteAsync(database, "UPDATE clients SET disabled_at = now() WHERE id = 'gamma'");

        var sender = Substitute.For<ISender>();
        if (renderFails)
            sender.Send(Arg.Any<GeneratePdfCommand>(), Arg.Any<CancellationToken>()).ThrowsAsync(new TemplateNotFoundException("invoice"));
        else
            sender.Send(Arg.Any<GeneratePdfCommand>(), Arg.Any<CancellationToken>())
                .Returns(new GeneratePdfResult("a7e0495b2cd24ed9bb65b1b0f3a14c20", "http://x/files/a7e0495b2cd24ed9bb65b1b0f3a14c20", 2048));

        var logs = new CapturingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(logging => logging.AddProvider(logs).SetMinimumLevel(LogLevel.Debug));
        services.AddSingleton(sender);
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton(Options.Create(new JobStoreOptions()));
        services.AddSingleton(Options.Create(new BlocksBusOptions { DefaultClientId = defaultClientId }));
        services.AddDbContext<PdfBuilderDbContext>(options => options.Configure(database.ConnectionString));
        services.AddScoped<IPdfJobStore, EfPdfJobStore>();
        services.AddScoped<ClientScope>();
        services.AddBlocksClientStore<PdfBuilderDbContext>();

        // What the stud registers for a block: one caller per scope, which the consumer sets from the message.
        services.AddScoped<FixedCurrentClient>();
        services.AddScoped<ICurrentClient>(provider => provider.GetRequiredService<FixedCurrentClient>());
        services.AddScoped<IClientContext>(provider => provider.GetRequiredService<FixedCurrentClient>());

        services.AddMassTransitTestHarness(x =>
        {
            x.AddConsumer<GeneratePdfConsumer>();
            x.AddEntityFrameworkOutbox<PdfBuilderDbContext>(outbox =>
            {
                outbox.UsePostgres();
                outbox.UseBusOutbox();
            });
        });

        var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.GetRequiredService<ITestHarness>().Start();
        return new Rig(provider, database, sender, logs);
    }

    private static async Task ExecuteAsync(TestDatabase database, string sql)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(Ct);
    }

    private static async Task<T?> ScalarAsync<T>(TestDatabase database, string sql, Guid jobId)
    {
        await using var connection = new NpgsqlConnection(database.ConnectionString);
        await connection.OpenAsync(Ct);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", jobId);
        var value = await command.ExecuteScalarAsync(Ct);
        return value is null or DBNull ? default : (T)value;
    }

    private static Task<string?> OwnerAsync(Rig rig, Guid jobId) =>
        ScalarAsync<string>(rig.Database, "SELECT client_id FROM pdf_jobs WHERE job_id = @id", jobId);

    private static Task<string?> RowAsync(Rig rig, Guid jobId) =>
        ScalarAsync<string>(rig.Database, "SELECT row_to_json(j)::text FROM pdf_jobs j WHERE job_id = @id", jobId);

    private static async Task<IGeneratePdfResponse> SendAsync(Rig rig, string? clientId, Guid? jobId = null)
    {
        var harness = rig.Provider.GetRequiredService<ITestHarness>();
        var request = new GeneratePdfRequest(jobId ?? NewId.NextGuid(), "invoice", "{}") { ClientId = clientId };
        var response = await harness.GetRequestClient<IGeneratePdfRequest>().GetResponse<IGeneratePdfResponse>(request, Ct);
        return response.Message;
    }

    private static async Task<PdfJob?> ReadAsAsync(Rig rig, string clientId, Guid jobId)
    {
        await using var scope = rig.Provider.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<IClientContext>().ActAs(clientId);
        return await scope.ServiceProvider.GetRequiredService<IPdfJobStore>().GetAsync(jobId, Ct);
    }

    private static async Task AssertRefusedAsync(Rig rig, IGeneratePdfResponse response, string code, string messagePart)
    {
        Assert.False(response.IsSuccess);
        Assert.Equal(code, response.ErrorCode);
        Assert.Contains(messagePart, response.ErrorMessage, StringComparison.Ordinal);

        // Nothing was rendered and nothing was recorded: no client could own a job.
        await rig.Sender.DidNotReceive().Send(Arg.Any<GeneratePdfCommand>(), Arg.Any<CancellationToken>());
        Assert.Equal(0L, await ScalarAsync<long>(rig.Database, "SELECT count(*) FROM pdf_jobs WHERE @id IS NOT NULL", Guid.Empty));
    }

    [Fact]
    public async Task A_message_naming_a_client_is_handled_for_that_client_and_only_it_can_read_the_job()
    {
        await using var rig = await StartAsync(defaultClientId: "alpha");
        var jobId = NewId.NextGuid();

        var response = await SendAsync(rig, "beta", jobId);

        Assert.True(response.IsSuccess);
        Assert.Equal("beta", await OwnerAsync(rig, jobId));
        Assert.Equal(PdfJobStatus.Succeeded, (await ReadAsAsync(rig, "beta", jobId))!.Status);
        Assert.Null(await ReadAsAsync(rig, "alpha", jobId));
    }

    [Fact]
    public async Task A_message_naming_no_client_is_handled_for_the_default_client()
    {
        await using var rig = await StartAsync(defaultClientId: "alpha");
        var jobId = NewId.NextGuid();

        var response = await SendAsync(rig, clientId: null, jobId);

        Assert.True(response.IsSuccess);
        Assert.Equal("alpha", await OwnerAsync(rig, jobId));
    }

    [Fact]
    public async Task A_message_naming_no_client_with_no_default_configured_is_refused()
    {
        await using var rig = await StartAsync(defaultClientId: null);

        var response = await SendAsync(rig, clientId: null);

        await AssertRefusedAsync(rig, response, PdfErrorCodes.ValidationFailed, "names no client");
    }

    [Fact]
    public async Task A_client_id_that_is_not_a_client_id_is_refused()
    {
        await using var rig = await StartAsync(defaultClientId: "alpha");

        var response = await SendAsync(rig, "Bad_Id");

        await AssertRefusedAsync(rig, response, PdfErrorCodes.ValidationFailed, "not a valid client id");
    }

    [Fact]
    public async Task A_client_that_is_not_registered_is_refused()
    {
        await using var rig = await StartAsync(defaultClientId: "alpha");

        var response = await SendAsync(rig, "nobody");

        await AssertRefusedAsync(rig, response, PdfErrorCodes.ValidationFailed, "'nobody' is not registered");
    }

    [Fact]
    public async Task A_default_client_that_is_not_registered_refuses_messages_that_name_no_client()
    {
        await using var rig = await StartAsync(defaultClientId: "ghost");

        var response = await SendAsync(rig, clientId: null);

        await AssertRefusedAsync(rig, response, PdfErrorCodes.ValidationFailed, "'ghost' is not registered");
    }

    [Fact]
    public async Task A_disabled_client_is_refused_with_the_code_the_api_uses()
    {
        await using var rig = await StartAsync(defaultClientId: "alpha");

        var response = await SendAsync(rig, "gamma");

        await AssertRefusedAsync(rig, response, PdfErrorCodes.ClientDisabled, "'gamma' is disabled");
    }

    [Fact]
    public async Task A_disabled_default_client_refuses_messages_that_name_no_client()
    {
        await using var rig = await StartAsync(defaultClientId: "gamma");

        var response = await SendAsync(rig, clientId: null);

        await AssertRefusedAsync(rig, response, PdfErrorCodes.ClientDisabled, "'gamma' is disabled");
    }

    [Fact]
    public async Task A_job_id_that_belongs_to_another_client_is_refused_and_that_job_is_not_touched()
    {
        await using var rig = await StartAsync(defaultClientId: "alpha");
        var jobId = NewId.NextGuid();
        Assert.True((await SendAsync(rig, "alpha", jobId)).IsSuccess);
        var before = await RowAsync(rig, jobId);
        rig.Sender.ClearReceivedCalls();

        var response = await SendAsync(rig, "beta", jobId);

        Assert.False(response.IsSuccess);
        Assert.Equal(PdfErrorCodes.ValidationFailed, response.ErrorCode);
        Assert.Contains("already used by another job", response.ErrorMessage, StringComparison.Ordinal);

        // Nothing in the answer describes the other job, and it is byte for byte as it was.
        Assert.DoesNotContain("alpha", response.ErrorMessage, StringComparison.OrdinalIgnoreCase);
        Assert.Null(response.FileId);
        Assert.Equal(before, await RowAsync(rig, jobId));
        await rig.Sender.DidNotReceive().Send(Arg.Any<GeneratePdfCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task The_same_client_sending_its_own_job_id_again_updates_its_own_job()
    {
        await using var rig = await StartAsync(defaultClientId: "alpha");
        var jobId = NewId.NextGuid();

        Assert.True((await SendAsync(rig, "alpha", jobId)).IsSuccess);
        Assert.True((await SendAsync(rig, "alpha", jobId)).IsSuccess);

        Assert.Equal("alpha", await OwnerAsync(rig, jobId));
        Assert.Equal(PdfJobStatus.Succeeded, (await ReadAsAsync(rig, "alpha", jobId))!.Status);
    }

    [Fact]
    public async Task A_message_handled_for_a_client_logs_that_client()
    {
        await using var rig = await StartAsync(defaultClientId: "alpha", renderFails: true);

        var response = await SendAsync(rig, "beta");

        Assert.Equal(PdfErrorCodes.TemplateNotFound, response.ErrorCode);
        var line = Assert.Single(rig.Logs.Entries, e => e.Message.StartsWith("PDF request rejected", StringComparison.Ordinal));
        Assert.Equal("beta", line.Scope["ClientId"]);
    }

    [Fact]
    public async Task A_malformed_client_id_is_not_written_to_the_log()
    {
        await using var rig = await StartAsync(defaultClientId: "alpha");

        await SendAsync(rig, "Bad_Id <script>");

        Assert.DoesNotContain(rig.Logs.Entries, e =>
            e.Message.Contains("<script>", StringComparison.Ordinal)
            || e.Scope.Values.Any(value => value?.ToString()?.Contains("<script>", StringComparison.Ordinal) == true));
    }
}
