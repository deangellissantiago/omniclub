using Checkin.Application.UseCases.Retention;

namespace Checkin.Api.BackgroundJobs;

/// <summary>
/// Dispara a régua de relacionamento automática (RetentionAlertService) — alunos sumidos,
/// aniversário e queda de movimento por ponto (ver README, "Régua de relacionamento"). Roda uma
/// vez ao subir e depois a cada <c>RetentionAlerts:IntervalHours</c> (default 24h). Cria seu
/// próprio escopo de DI a cada execução porque os repositórios são Scoped e isto é um singleton
/// de longa duração (mesma razão pela qual controllers não guardam repositório em campo estático).
/// </summary>
public class RetentionAlertHostedService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly ILogger<RetentionAlertHostedService> _logger;
    private readonly TimeSpan _interval;

    public RetentionAlertHostedService(IServiceProvider services, ILogger<RetentionAlertHostedService> logger, IConfiguration configuration)
    {
        _services = services;
        _logger = logger;
        var hours = configuration.GetValue("RetentionAlerts:IntervalHours", 24);
        _interval = TimeSpan.FromHours(hours);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_interval);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var retentionService = scope.ServiceProvider.GetRequiredService<RetentionAlertService>();
                await retentionService.RunDailyChecksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                // Nunca deixa uma falha aqui derrubar o processo inteiro — o resto da API
                // (login, check-ins, etc.) não tem nada a ver com a régua de relacionamento.
                _logger.LogError(ex, "Falha ao rodar a régua de relacionamento automática.");
            }

            if (!await timer.WaitForNextTickAsync(stoppingToken)) break;
        }
    }
}
