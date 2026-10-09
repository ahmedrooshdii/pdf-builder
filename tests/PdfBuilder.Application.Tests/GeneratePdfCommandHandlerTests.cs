using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Application.Features.GeneratePdf;
using PdfBuilder.Domain.Exceptions;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Tests;

public sealed class GeneratePdfCommandHandlerTests
{
    private static readonly byte[] PdfBytes = [1, 2, 3, 4];

    private readonly IPdfTemplateRepository _templates = Substitute.For<IPdfTemplateRepository>();
    private readonly ITemplateEngine _engine = Substitute.For<ITemplateEngine>();
    private readonly IPdfRenderer _renderer = Substitute.For<IPdfRenderer>();
    private readonly IFileStorage _storage = Substitute.For<IFileStorage>();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private GeneratePdfCommandHandler CreateHandler() =>
        new(_templates, _engine, _renderer, _storage, NullLogger<GeneratePdfCommandHandler>.Instance);

    [Fact]
    public async Task Renders_the_template_then_stores_the_result()
    {
        var template = new PdfTemplate("invoice", "<p>{{ total }}</p>");
        _templates.GetByIdAsync("invoice", Arg.Any<CancellationToken>()).Returns(template);
        _engine.RenderAsync(template.Html, "{\"total\":9}", Arg.Any<CancellationToken>()).Returns("<p>9</p>");
        _renderer.RenderAsync("<p>9</p>", template.Options, Arg.Any<CancellationToken>()).Returns(PdfBytes);
        _storage.SaveAsync(PdfBytes, "application/pdf", Arg.Any<CancellationToken>())
            .Returns(new StoredFile("abc", "http://localhost/files/abc", PdfBytes.Length));

        var result = await CreateHandler().Handle(new GeneratePdfCommand("invoice", "{\"total\":9}"), Ct);

        Assert.Equal("abc", result.FileId);
        Assert.Equal("http://localhost/files/abc", result.FileUrl);
        Assert.Equal(PdfBytes.Length, result.SizeBytes);
    }

    [Fact]
    public async Task Passes_the_page_setup_declared_by_the_template_to_the_renderer()
    {
        var options = PdfRenderOptions.Default with { Landscape = true, Format = "Letter" };
        var template = new PdfTemplate("label", "<p>x</p>", options);
        _templates.GetByIdAsync("label", Arg.Any<CancellationToken>()).Returns(template);
        _engine.RenderAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("<p>x</p>");
        _renderer.RenderAsync(Arg.Any<string>(), Arg.Any<PdfRenderOptions>(), Arg.Any<CancellationToken>()).Returns(PdfBytes);
        _storage.SaveAsync(Arg.Any<byte[]>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new StoredFile("id", "url", 4));

        await CreateHandler().Handle(new GeneratePdfCommand("label", "{}"), Ct);

        await _renderer.Received(1).RenderAsync(Arg.Any<string>(), options, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Fails_permanently_when_the_template_is_unknown()
    {
        _templates.GetByIdAsync("missing", Arg.Any<CancellationToken>()).Returns((PdfTemplate?)null);

        var exception = await Assert.ThrowsAsync<TemplateNotFoundException>(
            () => CreateHandler().Handle(new GeneratePdfCommand("missing", "{}"), Ct));

        Assert.False(exception.IsTransient);
        Assert.Equal("TEMPLATE_NOT_FOUND", exception.ErrorCode);
        await _renderer.DidNotReceiveWithAnyArgs().RenderAsync(default!, default!, Ct);
    }

    [Fact]
    public async Task Stores_nothing_when_rendering_fails()
    {
        var template = new PdfTemplate("invoice", "<p>x</p>");
        _templates.GetByIdAsync("invoice", Arg.Any<CancellationToken>()).Returns(template);
        _engine.RenderAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns("<p>x</p>");
        _renderer.RenderAsync(Arg.Any<string>(), Arg.Any<PdfRenderOptions>(), Arg.Any<CancellationToken>())
            .Returns<byte[]>(_ => throw new PdfRenderException("browser crashed"));

        await Assert.ThrowsAsync<PdfRenderException>(
            () => CreateHandler().Handle(new GeneratePdfCommand("invoice", "{}"), Ct));

        await _storage.DidNotReceiveWithAnyArgs().SaveAsync(default!, default!, Ct);
    }
}
