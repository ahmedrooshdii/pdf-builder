using PdfBuilder.Contracts;
using PdfBuilder.Domain.Exceptions;

namespace PdfBuilder.Application.Tests;

/// <summary>
/// The transient/permanent split decides whether a failed message is retried or answered straight
/// away. Getting it wrong is expensive in both directions, so it is pinned down here.
/// </summary>
public sealed class ExceptionPolicyTests
{
    public static TheoryData<PdfBuilderException, bool, string> Cases => new()
    {
        { new TemplateNotFoundException("nope"), false, PdfErrorCodes.TemplateNotFound },
        { new TemplateRenderException("bad syntax"), false, PdfErrorCodes.TemplateRenderFailed },
        { new PdfRenderException("chromium died"), true, PdfErrorCodes.RenderFailed }
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Each_failure_carries_its_retry_policy_and_error_code(
        PdfBuilderException exception, bool expectedTransient, string expectedCode)
    {
        Assert.Equal(expectedTransient, exception.IsTransient);
        Assert.Equal(expectedCode, exception.ErrorCode);
    }

    [Fact]
    public void No_domain_failure_is_ever_marked_transient()
    {
        var domainFailures = typeof(DomainException).Assembly
            .GetTypes()
            .Where(t => !t.IsAbstract && typeof(DomainException).IsAssignableFrom(t))
            .Select(t => (DomainException)Activator.CreateInstance(
                t, [.. t.GetConstructors()[0].GetParameters().Select(DefaultFor)])!);

        Assert.All(domainFailures, failure => Assert.False(failure.IsTransient));
    }

    private static object? DefaultFor(System.Reflection.ParameterInfo parameter) =>
        parameter.ParameterType == typeof(string) ? "x" : null;
}
