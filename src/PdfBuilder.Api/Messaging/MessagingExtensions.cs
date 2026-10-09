using System.ComponentModel.DataAnnotations;
using MassTransit;
using Microsoft.Extensions.Options;

namespace PdfBuilder.Api.Messaging;

public static class MessagingExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers the RabbitMQ bus and the consumer that does the rendering.</summary>
        public IServiceCollection AddMessaging(IConfiguration configuration)
        {
            var section = configuration.GetSection(RabbitMqOptions.Section);

            services.AddOptions<RabbitMqOptions>()
                .Bind(section)
                .ValidateDataAnnotations()
                .ValidateOnStart();

            // The bus is configured before the host starts, so this is read directly rather than
            // resolved from DI.
            var rabbit = section.Get<RabbitMqOptions>() ?? new RabbitMqOptions();

            // Checked here, at registration, rather than left to ValidateOnStart: bus construction
            // also happens at host start and gets there first, reporting a bare "address scheme is
            // not supported" from deep inside MassTransit that names neither the setting nor the file.
            var errors = rabbit.Validate(new ValidationContext(rabbit)).Select(e => e.ErrorMessage!).ToArray();
            if (errors.Length != 0)
                throw new OptionsValidationException(RabbitMqOptions.Section, typeof(RabbitMqOptions), errors);

            services.AddMassTransit(x =>
            {
                x.SetKebabCaseEndpointNameFormatter();
                x.AddConsumer<GeneratePdfConsumer, GeneratePdfConsumerDefinition>();

                x.UsingRabbitMq((context, cfg) =>
                {
                    ConfigureHost(cfg, rabbit);
                    cfg.ConfigureEndpoints(context);
                });
            });

            return services;
        }
    }

    private static void ConfigureHost(IRabbitMqBusFactoryConfigurator cfg, RabbitMqOptions rabbit)
    {
        if (rabbit.UsesConnectionString)
        {
            // MassTransit reads the credentials, the virtual host and the port from the URI, and
            // enables TLS for the amqps scheme. Hosted brokers hand out exactly this string.
            cfg.Host(new Uri(rabbit.ConnectionString!));
            return;
        }

        cfg.Host(rabbit.Host, rabbit.VirtualHost, h =>
        {
            h.Username(rabbit.Username);
            h.Password(rabbit.Password);
        });
    }
}
