using Blocks.ServiceDefaults.EntityFramework;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Infrastructure.Jobs;
using PdfBuilder.Infrastructure.Persistence;
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
            services.AddValidatedOptions<DatabaseOptions>(configuration, DatabaseOptions.Section);

            // First, so it starts first: the schema must be current before the message bus begins
            // consuming and before the web server begins listening.
            services.AddPersistence(configuration);

            services.AddMemoryCache();

            // All singletons: each is either stateless or explicitly thread-safe, and
            // PlaywrightPdfRenderer must be one because it owns the shared Chromium process.
            services.AddSingleton<ITemplateEngine, ScribanTemplateEngine>();
            services.AddSingleton<IBuiltInTemplateSource, FileSystemTemplateRepository>();
            services.AddScoped<IAuthoredTemplateStore, EfAuthoredTemplateStore>();
            services.AddSingleton<IPdfRenderer, PlaywrightPdfRenderer>();
            services.AddSingleton<IFileStorage, LocalFileStorage>();

            // Scoped, like the DbContext they write through: the job store and the bus outbox must
            // share one context per request or message so that a job and its message commit together.
            services.AddScoped<IPdfJobStore, EfPdfJobStore>();
            services.AddHostedService<JobRetentionSweeper>();

            services.AddHealthChecks()
                .AddCheck<TemplateStoreHealthCheck>("template-store", HealthStatus.Unhealthy, tags: ["ready"])
                .AddDbContextCheck<PdfBuilderDbContext>("database", HealthStatus.Unhealthy, tags: ["ready"]);

            return services;
        }

        /// <summary>
        /// The database and what keeps its schema current. The connection string is required and has
        /// no fallback: a quiet in-memory mode is exactly the failure this storage exists to remove.
        /// </summary>
        private IServiceCollection AddPersistence(IConfiguration configuration)
        {
            var connectionString = configuration.GetConnectionString(DatabaseOptions.ConnectionStringName);

            // Checked at registration, like the broker's settings, so the message names the key
            // instead of surfacing later as a provider error from the first query.
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new OptionsValidationException(
                    DatabaseOptions.Section,
                    typeof(DatabaseOptions),
                    [$"ConnectionStrings:{DatabaseOptions.ConnectionStringName} is required: the Npgsql connection string of this service's PostgreSQL database."]);

            services.AddDbContext<PdfBuilderDbContext>(options => options.Configure(connectionString));
            services.AddBlocksClientStore<PdfBuilderDbContext>();
            services.AddBlocksConsoleSessionStore<PdfBuilderDbContext>();
            services.AddBlocksDataProtection<PdfBuilderDbContext>();

            services.AddScoped<TemplateImporter>();
            services.AddHostedService<DatabaseStartup>();

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
