using Checkin.Application.Ports.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Checkin.Infrastructure.WhatsApp;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWhatsAppIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WhatsAppOptions>(configuration.GetSection("WhatsApp"));
        services.AddHttpClient<IWhatsAppSender, WhatsAppServiceClient>();
        return services;
    }
}
