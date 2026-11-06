using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Blocks.ServiceDefaults.Files;

/// <summary>How a block takes the file-link stud: register the settings, the signer and the clock.</summary>
public static class BlocksFilesExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Binds <c>Blocks:Files</c>, refuses to start when it is invalid, and registers the link signer.
        /// </summary>
        public IServiceCollection AddBlocksFiles()
        {
            services.AddOptions<BlocksFilesOptions>()
                .BindConfiguration(BlocksFilesOptions.Section)
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<BlocksFilesOptions>, BlocksFilesOptionsValidator>());
            services.TryAddSingleton(TimeProvider.System);
            services.TryAddSingleton<FileLinkSigner>();
            return services;
        }
    }
}
