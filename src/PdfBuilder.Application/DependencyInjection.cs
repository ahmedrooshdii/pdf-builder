using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using PdfBuilder.Application.Behaviors;

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
            return services;
        }
    }
}
