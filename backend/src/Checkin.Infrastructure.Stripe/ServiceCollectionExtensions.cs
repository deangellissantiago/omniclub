using Checkin.Application.Ports.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Checkin.Infrastructure.Stripe;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddBillingIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<BillingOptions>(configuration.GetSection("Billing"));
        services.AddScoped<IBillingGateway, StripeBillingGateway>();
        return services;
    }
}
