using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PdfBuilder.Application;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Jobs;
using PdfBuilder.Domain.Templates;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>
/// User Story 4: two instances of the service sharing one database. Each is a complete service
/// provider built the way the real host builds it (<c>AddApplication</c> and <c>AddInfrastructure</c>),
/// with nothing shared between them but the database.
/// </summary>
public sealed class MultiInstanceTests(PostgresFixture postgres) : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 3, 9, 0, 0, TimeSpan.Zero);

    private readonly string _scratch = Path.Combine(Path.GetTempPath(), $"pdf-builder-instances-{Guid.NewGuid():N}");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose()
    {
        if (Directory.Exists(_scratch))
            Directory.Delete(_scratch, recursive: true);
    }

    private ServiceProvider Instance(TestDatabase database, string name)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Database"] = database.ConnectionString,
                ["TemplateStore:RootPath"] = Path.Combine(_scratch, name, "built-in"),
                ["TemplateStore:AuthoredRootPath"] = Path.Combine(_scratch, name, "authored"),
                ["Storage:RootPath"] = Path.Combine(_scratch, name, "files"),
                ["Storage:PublicBaseUrl"] = $"http://{name}.test"
            })
            .Build();

        return new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddApplication()
            .AddInfrastructure(configuration)
            .BuildServiceProvider(validateScopes: true);
    }

    [Fact]
    public async Task A_job_accepted_by_one_instance_is_polled_through_another()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var instanceA = Instance(database, "a");
        await using var instanceB = Instance(database, "b");
        var jobId = Guid.NewGuid();

        await using (var scope = instanceA.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPdfJobStore>().SaveAsync(PdfJob.Enqueued(jobId, "invoice", Now), Ct);

        await using (var scope = instanceB.CreateAsyncScope())
        {
            var seenByB = await scope.ServiceProvider.GetRequiredService<IPdfJobStore>().GetAsync(jobId, Ct);
            Assert.Equal(PdfJobStatus.Pending, seenByB!.Status);
        }

        // B's worker finishes it; A, polled next, reports the result.
        await using (var scope = instanceB.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<IPdfJobStore>()
                .SaveAsync(PdfJob.Enqueued(jobId, "invoice", Now).Succeed("f1", "http://b.test/files/f1", 2048, Now.AddSeconds(2)), Ct);

        await using (var scope = instanceA.CreateAsyncScope())
        {
            var seenByA = await scope.ServiceProvider.GetRequiredService<IPdfJobStore>().GetAsync(jobId, Ct);
            Assert.Equal(PdfJobStatus.Succeeded, seenByA!.Status);
            Assert.Equal("f1", seenByA.FileId);
        }
    }

    [Fact]
    public async Task A_template_published_through_one_instance_is_what_the_other_renders()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await using var instanceA = Instance(database, "a");
        await using var instanceB = Instance(database, "b");

        var options = PdfRenderOptions.Default;
        var draft = AuthoredTemplate.Create("letter", new TemplateContent("<p>v1</p>", "", "{}", options), Now);

        await using (var scope = instanceA.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAuthoredTemplateStore>();
            await store.AddAsync(draft, Ct);

            // Nothing is published yet, so jobs on the other instance cannot use it.
            await using var other = instanceB.CreateAsyncScope();
            Assert.Null(await other.ServiceProvider.GetRequiredService<IPdfTemplateRepository>().GetByKeyAsync("letter", Ct));

            await store.UpdateAsync(draft.Publish(1, Now), 1, Ct);
        }

        await using (var scope = instanceB.CreateAsyncScope())
        {
            var rendered = await scope.ServiceProvider.GetRequiredService<IPdfTemplateRepository>().GetByKeyAsync("letter", Ct);
            Assert.Contains("v1", rendered!.Html, StringComparison.Ordinal);
        }

        // An edit published through B replaces what A renders.
        await using (var scope = instanceB.CreateAsyncScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<IAuthoredTemplateStore>();
            var current = (await store.GetAsync("letter", Ct))!;
            var edited = current.SaveDraft(current.Draft with { Html = "<p>v2</p>" }, current.Revision, Now.AddMinutes(1))
                .Publish(current.Revision + 1, Now.AddMinutes(1));
            await store.UpdateAsync(edited, current.Revision, Ct);
        }

        await using (var scope = instanceA.CreateAsyncScope())
        {
            var rendered = await scope.ServiceProvider.GetRequiredService<IPdfTemplateRepository>().GetByKeyAsync("letter", Ct);
            Assert.Contains("v2", rendered!.Html, StringComparison.Ordinal);
        }
    }
}
