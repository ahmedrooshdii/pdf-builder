using System.Collections.Concurrent;
using MassTransit;
using MassTransit.Testing;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PdfBuilder.Api.Messaging;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Application.Features.GeneratePdf;
using PdfBuilder.Contracts;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Jobs;

namespace PdfBuilder.Api.Tests;

/// <summary>
/// A transient failure is retried, then faulted. The HTTP caller polling the job has to see that
/// end state too, not a job that stays Pending forever.
/// </summary>
public sealed class GeneratePdfConsumerRetryTests
{
    private sealed class DictionaryJobStore : IPdfJobStore
    {
        public ConcurrentDictionary<Guid, PdfJob> Jobs { get; } = new();

        public Task SaveAsync(PdfJob job, CancellationToken cancellationToken = default)
        {
            Jobs[job.JobId] = job;
            return Task.CompletedTask;
        }

        public Task<PdfJob?> GetAsync(Guid jobId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Jobs.TryGetValue(jobId, out var job) ? job : null);
    }

    [Fact]
    public async Task Exhausted_transient_failure_fails_the_job_and_still_faults()
    {
        var ct = TestContext.Current.CancellationToken;
        var sender = Substitute.For<ISender>();
        sender.Send(Arg.Any<GeneratePdfCommand>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new PdfRenderException("chromium died"));
        var jobs = new DictionaryJobStore();

        await using var provider = new ServiceCollection()
            .AddSingleton(sender)
            .AddSingleton<IPdfJobStore>(jobs)
            .AddSingleton(TimeProvider.System)
            .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
            .AddMassTransitTestHarness(x => x.AddConsumer<GeneratePdfConsumer>(c =>
                c.UseMessageRetry(r => r.Immediate(GeneratePdfConsumerDefinition.RetryLimit))))
            .BuildServiceProvider(true);
        var harness = provider.GetRequiredService<ITestHarness>();
        await harness.Start();

        var jobId = NewId.NextGuid();
        await jobs.SaveAsync(PdfJob.Enqueued(jobId, "invoice", DateTimeOffset.UtcNow), ct);

        await harness.Bus.Publish<IGeneratePdfRequest>(new GeneratePdfRequest(jobId, "invoice", "{}"), ct);

        Assert.True(await harness.Published.Any<Fault<IGeneratePdfRequest>>(ct));
        await sender.Received(GeneratePdfConsumerDefinition.RetryLimit + 1)
            .Send(Arg.Any<GeneratePdfCommand>(), Arg.Any<CancellationToken>());

        var job = jobs.Jobs[jobId];
        Assert.Equal(PdfJobStatus.Failed, job.Status);
        Assert.Equal(PdfErrorCodes.RenderFailed, job.ErrorCode);
    }
}
