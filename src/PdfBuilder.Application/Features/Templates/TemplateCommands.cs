using FluentValidation;
using MediatR;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Features.Templates;

// ---- Create ---------------------------------------------------------------------------------

/// <summary>Starts a new draft. Without <paramref name="Content"/>, it starts from a small working example.</summary>
public sealed record CreateTemplateCommand(string TemplateKey, TemplateContent? Content) : IRequest<TemplateView>;

public sealed class CreateTemplateCommandValidator : AbstractValidator<CreateTemplateCommand>
{
    public CreateTemplateCommandValidator()
    {
        RuleFor(x => x.TemplateKey).ValidTemplateKey();
        RuleFor(x => x.Content!).SetValidator(new TemplateContentValidator()).When(x => x.Content is not null);
    }
}

public sealed class CreateTemplateCommandHandler(TemplateCatalog catalog, IAuthoredTemplateStore store, TimeProvider clock)
    : IRequestHandler<CreateTemplateCommand, TemplateView>
{
    public async Task<TemplateView> Handle(CreateTemplateCommand request, CancellationToken cancellationToken)
    {
        await catalog.EnsureKeyIsFreeAsync(request.TemplateKey, cancellationToken);

        var template = AuthoredTemplate.Create(
            request.TemplateKey, request.Content ?? TemplateStarter.Content, clock.GetUtcNow());

        await store.AddAsync(template, cancellationToken);
        return TemplateView.From(template);
    }
}

// ---- Duplicate ------------------------------------------------------------------------------

/// <summary>
/// Copies a template's draft into a new draft under another key. The way to change a built-in
/// template, and to branch an authored one.
/// </summary>
public sealed record DuplicateTemplateCommand(string SourceKey, string TemplateKey) : IRequest<TemplateView>;

public sealed class DuplicateTemplateCommandValidator : AbstractValidator<DuplicateTemplateCommand>
{
    public DuplicateTemplateCommandValidator()
    {
        RuleFor(x => x.SourceKey).ValidTemplateKey();
        RuleFor(x => x.TemplateKey).ValidTemplateKey();
    }
}

public sealed class DuplicateTemplateCommandHandler(TemplateCatalog catalog, IAuthoredTemplateStore store, TimeProvider clock)
    : IRequestHandler<DuplicateTemplateCommand, TemplateView>
{
    public async Task<TemplateView> Handle(DuplicateTemplateCommand request, CancellationToken cancellationToken)
    {
        var source = await catalog.GetAsync(request.SourceKey, cancellationToken);
        await catalog.EnsureKeyIsFreeAsync(request.TemplateKey, cancellationToken);

        var copy = AuthoredTemplate.Create(request.TemplateKey, source.Draft, clock.GetUtcNow());
        await store.AddAsync(copy, cancellationToken);
        return TemplateView.From(copy);
    }
}

// ---- Save draft -----------------------------------------------------------------------------

/// <summary>Replaces the draft. Jobs keep rendering the published version until the next publish.</summary>
/// <param name="Revision">The revision the edit was based on; a stale one is refused.</param>
public sealed record SaveTemplateDraftCommand(string TemplateKey, int Revision, TemplateContent Content)
    : IRequest<TemplateView>;

public sealed class SaveTemplateDraftCommandValidator : AbstractValidator<SaveTemplateDraftCommand>
{
    public SaveTemplateDraftCommandValidator()
    {
        RuleFor(x => x.TemplateKey).ValidTemplateKey();
        RuleFor(x => x.Revision).GreaterThan(0);
        RuleFor(x => x.Content).NotNull().SetValidator(new TemplateContentValidator());
    }
}

public sealed class SaveTemplateDraftCommandHandler(TemplateCatalog catalog, IAuthoredTemplateStore store, TimeProvider clock)
    : IRequestHandler<SaveTemplateDraftCommand, TemplateView>
{
    public async Task<TemplateView> Handle(SaveTemplateDraftCommand request, CancellationToken cancellationToken)
    {
        var current = await catalog.GetEditableAsync(request.TemplateKey, cancellationToken);
        var saved = current.SaveDraft(request.Content, request.Revision, clock.GetUtcNow());

        if (saved != current)
            await store.UpdateAsync(saved, current.Revision, cancellationToken);

        return TemplateView.From(saved);
    }
}

// ---- Publish --------------------------------------------------------------------------------

/// <summary>
/// Makes the draft what jobs render, after proving it renders: the draft is bound to its own
/// sample data first, and a template that fails is refused rather than published broken.
/// </summary>
public sealed record PublishTemplateCommand(string TemplateKey, int Revision) : IRequest<TemplateView>;

public sealed class PublishTemplateCommandValidator : AbstractValidator<PublishTemplateCommand>
{
    public PublishTemplateCommandValidator()
    {
        RuleFor(x => x.TemplateKey).ValidTemplateKey();
        RuleFor(x => x.Revision).GreaterThan(0);
    }
}

public sealed class PublishTemplateCommandHandler(
    TemplateCatalog catalog,
    IAuthoredTemplateStore store,
    ITemplateEngine engine,
    TimeProvider clock)
    : IRequestHandler<PublishTemplateCommand, TemplateView>
{
    public async Task<TemplateView> Handle(PublishTemplateCommand request, CancellationToken cancellationToken)
    {
        var current = await catalog.GetEditableAsync(request.TemplateKey, cancellationToken);

        // Throws TemplateRenderException with the engine's message, which names the line at fault.
        await engine.RenderAsync(current.Draft.ComposeHtml(), current.Draft.SampleOrEmpty, cancellationToken);

        var published = current.Publish(request.Revision, clock.GetUtcNow());
        if (published != current)
            await store.UpdateAsync(published, current.Revision, cancellationToken);

        return TemplateView.From(published);
    }
}

// ---- Delete ---------------------------------------------------------------------------------

/// <summary>Removes an authored template. Jobs that name it fail with TEMPLATE_NOT_FOUND from then on.</summary>
public sealed record DeleteTemplateCommand(string TemplateKey) : IRequest;

public sealed class DeleteTemplateCommandHandler(TemplateCatalog catalog, IAuthoredTemplateStore store)
    : IRequestHandler<DeleteTemplateCommand>
{
    public async Task Handle(DeleteTemplateCommand request, CancellationToken cancellationToken)
    {
        // Resolves first so a built-in key is refused as read-only, not reported as missing.
        await catalog.GetEditableAsync(request.TemplateKey, cancellationToken);
        await store.DeleteAsync(request.TemplateKey, cancellationToken);
    }
}

// ---- Preview --------------------------------------------------------------------------------

/// <summary>
/// Binds content to its sample data and returns the HTML, without storing anything or starting a
/// browser. Fast enough to run as someone types; the PDF itself still comes from a job.
/// </summary>
public sealed record PreviewTemplateCommand(TemplateContent Content) : IRequest<string>;

public sealed class PreviewTemplateCommandValidator : AbstractValidator<PreviewTemplateCommand>
{
    public PreviewTemplateCommandValidator()
    {
        RuleFor(x => x.Content).NotNull().SetValidator(new TemplateContentValidator());
    }
}

public sealed class PreviewTemplateCommandHandler(ITemplateEngine engine) : IRequestHandler<PreviewTemplateCommand, string>
{
    public Task<string> Handle(PreviewTemplateCommand request, CancellationToken cancellationToken) =>
        engine.RenderAsync(request.Content.ComposeHtml(), request.Content.SampleOrEmpty, cancellationToken);
}
