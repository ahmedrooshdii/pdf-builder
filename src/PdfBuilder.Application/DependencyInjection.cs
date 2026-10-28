using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PdfBuilder.Application.Abstractions;
using PdfBuilder.Application.Behaviors;
using PdfBuilder.Application.Features.Templates;

namespace PdfBuilder.Application;

public static class DependencyInjection
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the MediatR pipeline and every validator in this assembly.</summary>
        public IServiceCollection AddApplication()
        {
            var assembly = typeof(DependencyInjection).Assembly;

            services.AddMediatR(cfg =>
            {
                cfg.RegisterServicesFromAssembly(assembly);
                cfg.AddOpenBehavior(typeof(ValidationBehavior<,>));
            });

            services.AddValidatorsFromAssembly(assembly, includeInternalTypes: false);

            // The render pipeline sees published templates only; the catalog sees everything, for editing.
            services.AddSingleton<TemplateCatalog>();
            services.AddSingleton<IPdfTemplateRepository, PublishedTemplateRepository>();

            return services;
        }
    }
}
