using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Application.Features.Templates;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Tests;

public sealed class TemplateCommandTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 28, 12, 0, 0, TimeSpan.Zero);
    private static readonly TemplateContent Content = new("<p>{{ a }}</p>", "", "{\"a\":1}", PdfRenderOptions.Default);

    private readonly IAuthoredTemplateStore _store = Substitute.For<IAuthoredTemplateStore>();
    private readonly IBuiltInTemplateSource _builtIn = Substitute.For<IBuiltInTemplateSource>();
    private readonly ITemplateEngine _engine = Substitute.For<ITemplateEngine>();
    private readonly TimeProvider _clock = new FixedClock(Now);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private TemplateCatalog Catalog => new(_store, _builtIn);

    public TemplateCommandTests()
    {
        _store.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
        _builtIn.ListAsync(Arg.Any<CancellationToken>()).Returns([]);
    }

    private void GivenBuiltIn(string key)
    {
        var template = new BuiltInTemplate(key, Content, Now);
        _builtIn.ListAsync(Arg.Any<CancellationToken>()).Returns([template]);
        _builtIn.GetByKeyAsync(key, Arg.Any<CancellationToken>()).Returns(template);
    }

    private AuthoredTemplate GivenAuthored(AuthoredTemplate template)
    {
        _store.ListAsync(Arg.Any<CancellationToken>()).Returns([template]);
        _store.GetAsync(template.Key, Arg.Any<CancellationToken>()).Returns(template);
        return template;
    }

    [Fact]
    public async Task Creating_refuses_a_key_a_built_in_template_has_in_another_case()
    {
        GivenBuiltIn("invoice");

        var handler = new CreateTemplateCommandHandler(Catalog, _store, _clock);

        await Assert.ThrowsAsync<TemplateKeyTakenException>(() =>
            handler.Handle(new CreateTemplateCommand("INVOICE", null), Ct));
        await _store.DidNotReceive().AddAsync(Arg.Any<AuthoredTemplate>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Creating_without_content_starts_from_a_working_example()
    {
        var view = await new CreateTemplateCommandHandler(Catalog, _store, _clock)
            .Handle(new CreateTemplateCommand("letter", null), Ct);

        Assert.Equal(TemplateStatus.Draft, view.Status);
        Assert.Contains("{{ title }}", view.Draft.Html);
        Assert.NotEmpty(view.Draft.SampleJson);
        await _store.Received(1).AddAsync(Arg.Is<AuthoredTemplate>(t => t.Key == "letter"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publishing_a_draft_that_does_not_render_publishes_nothing()
    {
        GivenAuthored(AuthoredTemplate.Create("letter", Content, Now));
        _engine.RenderAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new TemplateRenderException("Template syntax error"));

        var handler = new PublishTemplateCommandHandler(Catalog, _store, _engine, _clock);

        await Assert.ThrowsAsync<TemplateRenderException>(() => handler.Handle(new PublishTemplateCommand("letter", 1), Ct));
        await _store.DidNotReceive().UpdateAsync(Arg.Any<AuthoredTemplate>(), Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Publishing_checks_the_draft_against_its_own_sample_data()
    {
        GivenAuthored(AuthoredTemplate.Create("letter", Content, Now));

        var view = await new PublishTemplateCommandHandler(Catalog, _store, _engine, _clock)
            .Handle(new PublishTemplateCommand("letter", 1), Ct);

        Assert.Equal(TemplateStatus.Published, view.Status);
        await _engine.Received(1).RenderAsync(Content.Html, Content.SampleJson, Arg.Any<CancellationToken>());
        await _store.Received(1).UpdateAsync(Arg.Is<AuthoredTemplate>(t => t.Published == Content), 1, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Built_in_templates_cannot_be_saved_published_or_deleted()
    {
        GivenBuiltIn("invoice");

        await Assert.ThrowsAsync<TemplateReadOnlyException>(() =>
            new SaveTemplateDraftCommandHandler(Catalog, _store, _clock)
                .Handle(new SaveTemplateDraftCommand("invoice", 1, Content), Ct));
        await Assert.ThrowsAsync<TemplateReadOnlyException>(() =>
            new PublishTemplateCommandHandler(Catalog, _store, _engine, _clock)
                .Handle(new PublishTemplateCommand("invoice", 1), Ct));
        await Assert.ThrowsAsync<TemplateReadOnlyException>(() =>
            new DeleteTemplateCommandHandler(Catalog, _store).Handle(new DeleteTemplateCommand("invoice"), Ct));
    }

    [Fact]
    public async Task Duplicating_a_built_in_template_gives_an_editable_draft_with_its_content()
    {
        GivenBuiltIn("invoice");

        var copy = await new DuplicateTemplateCommandHandler(Catalog, _store, _clock)
            .Handle(new DuplicateTemplateCommand("invoice", "invoice-v2"), Ct);

        Assert.Equal(TemplateSource.Authored, copy.Source);
        Assert.Equal(TemplateStatus.Draft, copy.Status);
        Assert.Equal(Content, copy.Draft);
    }

    [Fact]
    public async Task Jobs_never_see_a_draft_that_was_not_published()
    {
        GivenAuthored(AuthoredTemplate.Create("letter", Content, Now));

        Assert.Null(await new PublishedTemplateRepository(_store, _builtIn).GetByKeyAsync("letter", Ct));
    }

    [Fact]
    public async Task Jobs_see_the_published_version_not_later_draft_edits()
    {
        var edited = AuthoredTemplate.Create("letter", Content, Now)
            .Publish(1, Now)
            .SaveDraft(Content with { Html = "<p>work in progress</p>" }, 2, Now);
        GivenAuthored(edited);

        var template = await new PublishedTemplateRepository(_store, _builtIn).GetByKeyAsync("letter", Ct);

        Assert.Equal(Content.Html, template!.Html);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
