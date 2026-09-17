using Checkin.Application.UseCases.Webhooks;

namespace Checkin.Api.BackgroundJobs;

/// <summary>
/// Processa em background os webhooks gravados por WellhubWebhookController (ver WebhookEvent,
/// "Por quê") — drena a fila até esvaziar, depois espera um sinal de "chegou coisa nova" (quase
/// instantâneo) OU um tick de segurança (5s, pra pegar qualquer evento que passou batido — ex.:
/// o próprio processo reiniciou com itens pendentes no Mongo).
/// </summary>
public class WebhookProcessingHostedService : BackgroundService
{
    private static readonly TimeSpan FallbackPollInterval = TimeSpan.FromSeconds(5);

    private readonly IServiceProvider _services;
    private readonly IWebhookEventSignal _signal;
    private readonly ILogger<WebhookProcessingHostedService> _logger;

    public WebhookProcessingHostedService(IServiceProvider services, IWebhookEventSignal signal, ILogger<WebhookProcessingHostedService> logger)
    {
        _services = services;
        _signal = signal;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                int processedInThisPass;
                do
                {
                    using var scope = _services.CreateScope();
                    var processor = scope.ServiceProvider.GetRequiredService<WebhookProcessingService>();
                    processedInThisPass = await processor.ProcessPendingBatchAsync(ct: stoppingToken);
                } while (processedInThisPass > 0 && !stoppingToken.IsCancellationRequested);
            }
            catch (Exception ex)
            {
                // Não deixa uma falha de infraestrutura (ex.: Mongo temporariamente fora) matar o
                // loop — cada evento já trata sua própria falha (ver WebhookProcessingService);
                // isto aqui é só rede de segurança pra erro fora desse escopo.
                _logger.LogError(ex, "Falha inesperada no processamento de webhooks em background.");
            }

            try
            {
                await Task.WhenAny(_signal.WaitAsync(stoppingToken), Task.Delay(FallbackPollInterval, stoppingToken));
            }
            catch (OperationCanceledException)
            {
                // shutdown normal
            }
        }
    }
}
