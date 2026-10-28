using Microsoft.Extensions.Diagnostics.HealthChecks;
using PdfBuilder.Application.Abstractions;

namespace PdfBuilder.Infrastructure.Templates;

/// <summary>
/// Readiness probe for the built-in template directory. Catches the most common deployment mistake
/// by far: the volume holding the templates was not mounted, which would otherwise show up as every
/// job for a built-in template failing with TEMPLATE_NOT_FOUND.
/// </summary>
internal sealed class TemplateStoreHealthCheck(IBuiltInTemplateSource builtIn) : IHealthCheck
{
    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        if (builtIn is not FileSystemTemplateRepository fileSystem)
            return Task.FromResult(HealthCheckResult.Healthy("Template store is not file-system backed."));

        var root = fileSystem.RootPath;

        return Task.FromResult(Directory.Exists(root)
            ? HealthCheckResult.Healthy($"Template store is readable at '{root}'.")
            : new HealthCheckResult(context.Registration.FailureStatus, $"Template directory '{root}' does not exist."));
    }
}
