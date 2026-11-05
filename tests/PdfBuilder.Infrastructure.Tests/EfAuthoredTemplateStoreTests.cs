using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Templates;
using PdfBuilder.Infrastructure.Persistence;
using PdfBuilder.Infrastructure.Templates;
using PdfBuilder.TestSupport;

namespace PdfBuilder.Infrastructure.Tests;

/// <summary>
/// The authored-template store against a real PostgreSQL, covering what the file store was trusted
/// to do. Each call below gets a new <c>DbContext</c>, which is what a restart, or another instance,
/// looks like to it.
/// </summary>
public sealed class EfAuthoredTemplateStoreTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);

    private static readonly TemplateContent Content = new(
        "<p>{{ a }}</p>",
        "p { color: red; }",
        "{ \"a\": 1 }",
        PdfRenderOptions.Default with { Landscape = true, Margins = new PageMargins(Top: "5mm") });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    /// <summary>A store for one client, on a context of its own: what a request or a restart looks like to it.</summary>
    private static EfAuthoredTemplateStore StoreOn(TestDatabase database, string? clientId = "alpha")
    {
        var caller = new FixedCurrentClient(clientId);
        return new EfAuthoredTemplateStore(database.CreateContext(caller), new ClientScope(caller));
    }

    [Fact]
    public async Task Reads_back_exactly_what_was_stored_after_a_restart()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        var template = AuthoredTemplate.Create("letter", Content, Now).Publish(1, Now);

        await StoreOn(database).AddAsync(template, Ct);

        Assert.Equal(template, await StoreOn(database).GetAsync("letter", Ct));
        Assert.Equal(["letter"], (await StoreOn(database).ListAsync(Ct)).Select(t => t.Key));
    }

    [Fact]
    public async Task A_draft_ahead_of_the_published_version_stays_modified()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        var published = AuthoredTemplate.Create("letter", Content, Now).Publish(1, Now);
        await StoreOn(database).AddAsync(published, Ct);

        var edited = published.SaveDraft(Content with { Html = "<p>newer</p>" }, published.Revision, Now.AddMinutes(5));
        await StoreOn(database).UpdateAsync(edited, published.Revision, Ct);

        var stored = await StoreOn(database).GetAsync("letter", Ct);
        Assert.Equal(TemplateStatus.Modified, stored!.Status);
        Assert.Equal("<p>newer</p>", stored.Draft.Html);
        Assert.Equal(Content.Html, stored.Published!.Html);
    }

    [Fact]
    public async Task Is_empty_before_anything_is_written()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");

        Assert.Empty(await StoreOn(database).ListAsync(Ct));
        Assert.Null(await StoreOn(database).GetAsync("letter", Ct));
    }

    [Fact]
    public async Task Refuses_to_add_a_key_twice()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        await StoreOn(database).AddAsync(AuthoredTemplate.Create("letter", Content, Now), Ct);

        await Assert.ThrowsAsync<TemplateKeyTakenException>(() =>
            StoreOn(database).AddAsync(AuthoredTemplate.Create("letter", Content, Now), Ct));
    }

    [Fact]
    public async Task Refuses_the_same_key_in_another_letter_case()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        await StoreOn(database).AddAsync(AuthoredTemplate.Create("Invoice", Content, Now), Ct);

        await Assert.ThrowsAsync<TemplateKeyTakenException>(() =>
            StoreOn(database).AddAsync(AuthoredTemplate.Create("invoice", Content, Now), Ct));

        // Lookups stay exact: asking for the other spelling finds nothing.
        Assert.Null(await StoreOn(database).GetAsync("invoice", Ct));
        Assert.NotNull(await StoreOn(database).GetAsync("Invoice", Ct));
    }

    [Fact]
    public async Task A_refused_add_does_not_poison_the_context_for_the_next_save()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        await StoreOn(database).AddAsync(AuthoredTemplate.Create("letter", Content, Now), Ct);

        // One context, as in one request: a refused add followed by a valid one.
        var store = StoreOn(database);
        await Assert.ThrowsAsync<TemplateKeyTakenException>(() =>
            store.AddAsync(AuthoredTemplate.Create("letter", Content, Now), Ct));
        await store.AddAsync(AuthoredTemplate.Create("receipt", Content, Now), Ct);

        Assert.Equal(["letter", "receipt"], (await StoreOn(database).ListAsync(Ct)).Select(t => t.Key));
    }

    [Fact]
    public async Task Refuses_an_update_based_on_a_revision_that_is_no_longer_current()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        var original = AuthoredTemplate.Create("letter", Content, Now);
        await StoreOn(database).AddAsync(original, Ct);

        var first = original.SaveDraft(Content with { Html = "<p>one</p>" }, 1, Now);
        await StoreOn(database).UpdateAsync(first, expectedRevision: 1, Ct);

        // A second editor who also loaded revision 1 must not overwrite the first editor's save.
        var second = original.SaveDraft(Content with { Html = "<p>two</p>" }, 1, Now);
        var error = await Assert.ThrowsAsync<StaleTemplateRevisionException>(() =>
            StoreOn(database).UpdateAsync(second, expectedRevision: 1, Ct));

        Assert.Equal(2, error.CurrentRevision);
        Assert.Equal("<p>one</p>", (await StoreOn(database).GetAsync("letter", Ct))!.Draft.Html);
    }

    [Fact]
    public async Task Refuses_an_update_of_a_template_that_was_deleted_in_between()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        var original = AuthoredTemplate.Create("letter", Content, Now);
        await StoreOn(database).AddAsync(original, Ct);
        await StoreOn(database).DeleteAsync("letter", Ct);

        await Assert.ThrowsAsync<TemplateNotFoundException>(() =>
            StoreOn(database).UpdateAsync(original.SaveDraft(Content with { Html = "x" }, 1, Now), 1, Ct));
    }

    [Fact]
    public async Task Two_instances_saving_from_the_same_revision_cannot_both_win()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        var original = AuthoredTemplate.Create("letter", Content, Now);
        await StoreOn(database).AddAsync(original, Ct);

        var results = await Task.WhenAll(Enumerable.Range(0, 8).Select(async n =>
        {
            var edit = original.SaveDraft(Content with { Html = $"<p>{n}</p>" }, 1, Now);
            try
            {
                await StoreOn(database).UpdateAsync(edit, expectedRevision: 1, Ct);
                return true;
            }
            catch (StaleTemplateRevisionException)
            {
                return false;
            }
        }));

        Assert.Single(results, won => won);
        Assert.Equal(2, (await StoreOn(database).GetAsync("letter", Ct))!.Revision);
    }

    [Fact]
    public async Task A_template_published_through_one_instance_is_visible_to_another()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        var draft = AuthoredTemplate.Create("letter", Content, Now);
        var instanceA = StoreOn(database);
        var instanceB = StoreOn(database);
        await instanceA.AddAsync(draft, Ct);

        Assert.Null((await instanceB.GetAsync("letter", Ct))!.Published);
        await instanceA.UpdateAsync(draft.Publish(1, Now), 1, Ct);

        Assert.NotNull((await instanceB.GetAsync("letter", Ct))!.Published);
    }

    [Fact]
    public async Task Deletes_and_reports_whether_there_was_anything_to_delete()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        await StoreOn(database).AddAsync(AuthoredTemplate.Create("letter", Content, Now), Ct);

        Assert.True(await StoreOn(database).DeleteAsync("letter", Ct));
        Assert.False(await StoreOn(database).DeleteAsync("letter", Ct));
        Assert.Null(await StoreOn(database).GetAsync("letter", Ct));
    }

    [Theory]
    [InlineData("../letter")]
    [InlineData("sub/letter")]
    [InlineData("")]
    [InlineData("has space")]
    public async Task Never_resolves_a_key_that_is_not_a_valid_key(string key)
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");

        Assert.Null(await StoreOn(database).GetAsync(key, Ct));
        Assert.False(await StoreOn(database).DeleteAsync(key, Ct));
    }

    // ---- Feature 003: templates belong to the client that created them -----------------------------

    [Fact]
    public async Task Each_client_lists_only_its_own_templates()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        await StoreOn(database, "alpha").AddAsync(AuthoredTemplate.Create("letter", Content, Now), Ct);
        await StoreOn(database, "beta").AddAsync(AuthoredTemplate.Create("receipt", Content, Now), Ct);

        Assert.Equal(["letter"], (await StoreOn(database, "alpha").ListAsync(Ct)).Select(t => t.Key));
        Assert.Equal(["receipt"], (await StoreOn(database, "beta").ListAsync(Ct)).Select(t => t.Key));
    }

    [Fact]
    public async Task Another_clients_template_cannot_be_read_changed_or_deleted_and_is_left_as_it_was()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        var published = AuthoredTemplate.Create("letter", Content, Now).Publish(1, Now);
        await StoreOn(database, "alpha").AddAsync(published, Ct);
        var edited = published.SaveDraft(Content with { Html = "<p>hijacked</p>" }, published.Revision, Now.AddMinutes(1));

        var beta = StoreOn(database, "beta");

        Assert.Null(await beta.GetAsync("letter", Ct));
        // Not "someone else saved first": to beta the template does not exist at all.
        await Assert.ThrowsAsync<TemplateNotFoundException>(() => beta.UpdateAsync(edited, published.Revision, Ct));
        Assert.False(await beta.DeleteAsync("letter", Ct));

        Assert.Equal(published, await StoreOn(database, "alpha").GetAsync("letter", Ct));
    }

    [Fact]
    public async Task Two_clients_can_each_have_a_template_with_the_same_key_and_change_them_independently()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        var alphas = AuthoredTemplate.Create("invoice", Content with { Html = "<p>alpha</p>" }, Now);
        var betas = AuthoredTemplate.Create("invoice", Content with { Html = "<p>beta</p>" }, Now);

        await StoreOn(database, "alpha").AddAsync(alphas, Ct);
        await StoreOn(database, "beta").AddAsync(betas, Ct);

        // Deleting one leaves the other.
        Assert.True(await StoreOn(database, "alpha").DeleteAsync("invoice", Ct));
        Assert.Null(await StoreOn(database, "alpha").GetAsync("invoice", Ct));
        Assert.Equal("<p>beta</p>", (await StoreOn(database, "beta").GetAsync("invoice", Ct))!.Draft.Html);
    }

    [Fact]
    public async Task Within_a_client_a_key_is_taken_in_any_letter_case_but_another_client_may_use_it()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        await StoreOn(database, "alpha").AddAsync(AuthoredTemplate.Create("invoice", Content, Now), Ct);

        await Assert.ThrowsAsync<TemplateKeyTakenException>(() =>
            StoreOn(database, "alpha").AddAsync(AuthoredTemplate.Create("Invoice", Content, Now), Ct));

        await StoreOn(database, "beta").AddAsync(AuthoredTemplate.Create("INVOICE", Content, Now), Ct);
        Assert.Equal(["INVOICE"], (await StoreOn(database, "beta").ListAsync(Ct)).Select(t => t.Key));
    }

    [Fact]
    public async Task With_no_client_nothing_is_listed_and_nothing_can_be_added()
    {
        PostgresFixture.SkipUnlessEnabled();
        await using var database = await postgres.CreateDatabaseAsync();
        await database.RegisterClientsAsync("alpha", "beta");
        await StoreOn(database, "alpha").AddAsync(AuthoredTemplate.Create("letter", Content, Now), Ct);

        var nobody = StoreOn(database, clientId: null);

        Assert.Empty(await nobody.ListAsync(Ct));
        Assert.Null(await nobody.GetAsync("letter", Ct));
        await Assert.ThrowsAsync<ClientRequiredException>(() =>
            nobody.AddAsync(AuthoredTemplate.Create("other", Content, Now), Ct));
    }
}
