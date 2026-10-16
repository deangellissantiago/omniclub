using Checkin.Application.UseCases.Webhooks;

namespace Checkin.Api.BackgroundJobs;

/// <summary>
/// Envia em background os webhooks de saída gravados por OutboundWebhookService — drena tudo o
/// que está vencido e depois espera o próximo tick. Polling simples (5s) basta: o evento nasce
/// dentro do processamento do check-in, que já é assíncrono em relação ao Wellhub, então alguns
/// segundos de atraso não importam.
/// </summary>
public class OutboundWebhookHostedService : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _services;
    private readonly ILogger<OutboundWebhookHostedService> _logger;

    public OutboundWebhookHostedService(IServiceProvider services, ILogger<OutboundWebhookHostedService> logger)
    {
        _services = services;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int sent;
                do
                {
                    using var scope = _services.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<OutboundWebhookService>();
                    sent = await service.ProcessDueBatchAsync(ct: stoppingToken);
                } while (sent > 0 && !stoppingToken.IsCancellationRequested);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Falha inesperada no envio de webhooks de saída.");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // shutdown normal
            }
        }
    }
}
