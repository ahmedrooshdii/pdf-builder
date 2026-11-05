using System.Reflection;
using PdfBuilder.Application.Features.GeneratePdf;
using PdfBuilder.Contracts;
using PdfBuilder.Domain.Templates;

namespace PdfBuilder.Application.Tests;

/// <summary>
/// The published package makes promises other services code against: the limits match what the
/// service enforces, and the error-code catalogue is complete. These pin both.
/// </summary>
public sealed class ContractsTests
{
    [Fact]
    public void Error_code_catalogue_lists_every_constant()
    {
        var constants = typeof(PdfErrorCodes)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (string)f.GetRawConstantValue()!);

        Assert.Equal(constants.Order(), PdfErrorCodes.All.Order());
    }

    [Theory]
    [InlineData("VALIDATION_FAILED", true)]
    [InlineData("RENDER_FAILED", true)]
    [InlineData("render_failed", false)]
    [InlineData("SOMETHING_NEW", false)]
    [InlineData(null, false)]
    public void IsKnown_matches_the_catalogue_exactly(string? code, bool expected) =>
        Assert.Equal(expected, PdfErrorCodes.IsKnown(code));

    [Theory]
    [InlineData("invoice")]
    [InlineData("statement_v2")]
    [InlineData("-leading-dash")]
    [InlineData("dots.are.out")]
    [InlineData("../../etc/passwd")]
    [InlineData("")]
    public void Published_key_check_agrees_with_the_service(string key)
    {
        Assert.Equal(TemplateKey.IsValid(key), PdfRequestLimits.IsValidTemplateKey(key));
        Assert.Equal(
            new GeneratePdfCommandValidator().Validate(new GeneratePdfCommand(key, "{}")).IsValid,
            PdfRequestLimits.IsValidTemplateKey(key));
    }

    [Fact]
    public void Key_length_limit_is_the_one_in_the_pattern()
    {
        Assert.True(PdfRequestLimits.IsValidTemplateKey(new string('a', PdfRequestLimits.TemplateKeyMaxLength)));
        Assert.False(PdfRequestLimits.IsValidTemplateKey(new string('a', PdfRequestLimits.TemplateKeyMaxLength + 1)));
    }

    [Fact]
    public void Payload_limit_counts_utf8_bytes_not_characters()
    {
        // Two bytes per Arabic letter: half the limit in characters is already the whole limit in bytes.
        var atLimit = new string('ع', PdfRequestLimits.MaxPayloadBytes / 2);

        Assert.True(PdfRequestLimits.IsWithinPayloadLimit(atLimit));
        Assert.False(PdfRequestLimits.IsWithinPayloadLimit(atLimit + "ع"));
        Assert.False(PdfRequestLimits.IsWithinPayloadLimit(null));
    }

    [Fact]
    public void Request_rejects_null_members_at_construction()
    {
        Assert.Throws<ArgumentNullException>(() => new GeneratePdfRequest(Guid.NewGuid(), null!, "{}"));
        Assert.Throws<ArgumentNullException>(() => new GeneratePdfRequest(Guid.NewGuid(), "invoice", null!));
    }

    [Fact]
    public void The_client_disabled_code_is_published_and_known()
    {
        Assert.Equal("CLIENT_DISABLED", PdfErrorCodes.ClientDisabled);
        Assert.Contains(PdfErrorCodes.ClientDisabled, PdfErrorCodes.All);
        Assert.True(PdfErrorCodes.IsKnown("CLIENT_DISABLED"));
    }

    [Fact]
    public void A_request_names_no_client_unless_it_is_told_to()
    {
        var request = new GeneratePdfRequest(Guid.NewGuid(), "invoice", "{}");

        Assert.Null(request.ClientId);

        request.ClientId = "erp";
        Assert.Equal("erp", ((IGeneratePdfRequest)request).ClientId);
    }
}
