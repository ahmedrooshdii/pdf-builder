using PdfBuilder.Application.Features.GeneratePdf;

namespace PdfBuilder.Application.Tests;

public sealed class GeneratePdfCommandValidatorTests
{
    private readonly GeneratePdfCommandValidator _validator = new();

    [Theory]
    [InlineData("invoice")]
    [InlineData("credit-note")]
    [InlineData("statement_v2")]
    [InlineData("A1")]
    public void Accepts_well_formed_template_ids(string templateKey)
    {
        var result = _validator.Validate(new GeneratePdfCommand(templateKey, "{}"));

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("-leading-dash")]
    [InlineData("has space")]
    [InlineData("dots.are.out")]
    [InlineData("../../etc/passwd")]
    [InlineData("..")]
    [InlineData("sub/invoice")]
    [InlineData(@"sub\invoice")]
    public void Rejects_template_ids_that_could_escape_the_template_directory(string templateKey)
    {
        var result = _validator.Validate(new GeneratePdfCommand(templateKey, "{}"));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GeneratePdfCommand.TemplateKey));
    }

    [Fact]
    public void Rejects_template_ids_longer_than_64_characters()
    {
        var result = _validator.Validate(new GeneratePdfCommand(new string('a', 65), "{}"));

        Assert.False(result.IsValid);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[1, 2, 3]")]
    [InlineData("\"a string\"")]
    [InlineData("42")]
    public void Rejects_payloads_that_are_not_json_objects(string payload)
    {
        var result = _validator.Validate(new GeneratePdfCommand("invoice", payload));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(GeneratePdfCommand.JsonPayload));
    }

    [Fact]
    public void Rejects_payloads_over_the_size_limit()
    {
        var oversized = "{\"blob\":\"" + new string('x', GeneratePdfCommandValidator.MaxPayloadBytes) + "\"}";

        var result = _validator.Validate(new GeneratePdfCommand("invoice", oversized));

        Assert.False(result.IsValid);
    }
}
