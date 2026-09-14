using Checkin.Application.Ports.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Checkin.Infrastructure.Wellhub;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddWellhubIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<WellhubOptions>(configuration.GetSection("Wellhub"));
        services.AddHttpClient<IWellhubGateway, WellhubGatewayAdapter>();
        services.AddHttpClient<IWellhubBookingGateway, WellhubBookingGatewayAdapter>();
        return services;
    }
}
