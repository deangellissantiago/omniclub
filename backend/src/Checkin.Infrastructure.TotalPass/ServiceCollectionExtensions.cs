using Checkin.Application.Ports.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Checkin.Infrastructure.TotalPass;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddTotalPassIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<TotalPassOptions>(configuration.GetSection("TotalPass"));
        services.AddSingleton<TotalPassTokenCache>();
        // Sem seguir redirecionamentos: uma URL de confirmação que redireciona (ex.: pra tela de
        // login de um painel interno) terminaria num 200 de página HTML e seria lida como
        // "confirmado". Ver TotalPassGatewayAdapter.ConfirmCheckinAsync.
        services.AddHttpClient<ITotalPassGateway, TotalPassGatewayAdapter>(c => c.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        return services;
    }
}
