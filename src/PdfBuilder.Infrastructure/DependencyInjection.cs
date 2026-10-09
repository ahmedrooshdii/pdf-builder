using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Infrastructure.Jobs;
using PdfBuilder.Infrastructure.Rendering;
using PdfBuilder.Infrastructure.Storage;
using PdfBuilder.Infrastructure.Templates;

namespace PdfBuilder.Infrastructure;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>Binds configuration and wires one adapter to each port in Application/Abstractions.</summary>
        public IServiceCollection AddInfrastructure(IConfiguration configuration)
        {
            services.AddValidatedOptions<PlaywrightOptions>(configuration, PlaywrightOptions.Section);
            services.AddValidatedOptions<StorageOptions>(configuration, StorageOptions.Section);
            services.AddValidatedOptions<TemplateStoreOptions>(configuration, TemplateStoreOptions.Section);
            services.AddValidatedOptions<TemplateEngineOptions>(configuration, TemplateEngineOptions.Section);
            services.AddValidatedOptions<JobStoreOptions>(configuration, JobStoreOptions.Section);

            services.AddMemoryCache();

            // All singletons: each is either stateless or explicitly thread-safe, and
            // PlaywrightPdfRenderer must be one because it owns the shared Chromium process.
            services.AddSingleton<ITemplateEngine, ScribanTemplateEngine>();
            services.AddSingleton<IPdfTemplateRepository, FileSystemTemplateRepository>();
            services.AddSingleton<IPdfRenderer, PlaywrightPdfRenderer>();
            services.AddSingleton<IFileStorage, LocalFileStorage>();
            services.AddSingleton<IPdfJobStore, InMemoryPdfJobStore>();

            services.AddHealthChecks()
                .AddCheck<TemplateStoreHealthCheck>("template-store", HealthStatus.Unhealthy, tags: ["ready"]);

            return services;
        }

        /// <summary>
        /// Binds a section and validates it at startup, so a bad setting fails the deployment
        /// instead of surfacing as a runtime error on the first request that happens to need it.
        /// </summary>
        private IServiceCollection AddValidatedOptions<TOptions>(IConfiguration configuration, string section)
            where TOptions : class
        {
            services.AddOptions<TOptions>()
                .Bind(configuration.GetSection(section))
                .ValidateDataAnnotations()
                .ValidateOnStart();

            return services;
        }
    }
}
