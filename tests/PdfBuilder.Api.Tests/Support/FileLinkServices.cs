using Blocks.ServiceDefaults.Files;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using PdfBuilder.Api.Files;
using PdfBuilder.Infrastructure.Storage;

namespace PdfBuilder.Api.Tests.Support;

/// <summary>What hand-built service providers need for what the service does with file links.</summary>
internal static class FileLinkServices
{
    /// <summary>The options and the link maker the consumer and the job store ask for, configured like <see cref="ApiFactory"/>.</summary>
    public static IServiceCollection AddFileLinks(this IServiceCollection services, bool requireSignature = false)
    {
        services.AddSingleton(Options.Create(new BlocksFilesOptions
        {
            SigningKey = ApiFactory.FileSigningKey,
            RequireSignature = requireSignature
        }));
        services.AddSingleton(Options.Create(new StorageOptions { PublicBaseUrl = "http://x.test" }));
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<FileLinkSigner>();
        services.AddSingleton<PdfFileLinks>();
        return services;
    }
}
