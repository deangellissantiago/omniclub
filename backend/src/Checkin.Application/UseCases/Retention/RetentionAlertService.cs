using System.Globalization;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;
// ILogger é a única dependência externa do Application neste projeto (o resto do Domain/
// Application não conhece Mongo/ASP.NET Core/HTTP, ver README) — é uma abstração sem framework
// concreto por trás, e sem ela um job de background sem UI própria (este) fica sem nenhuma
// visibilidade de erro; vale a exceção.
using Microsoft.Extensions.Logging;

namespace Checkin.Application.UseCases.Retention;

/// <summary>
/// Régua de relacionamento automática (Fase 3 do roadmap) — três alertas via WhatsApp, sempre
/// mensagem de <b>template</b> pré-aprovado (ver IWhatsAppSender):
///
///   1. Aluno sumido (14+ dias sem check-in, cooldown de 14 dias entre alertas do mesmo aluno)
///   2. Aniversário do aluno (uma vez por ano, se tiver BirthDate cadastrado)
///   3. Queda de movimento de um ponto (últimos 7 dias vs. 7 dias anteriores, cooldown de 7 dias)
///
/// Chamado uma vez por dia por RetentionAlertHostedService (Checkin.Api) — não é um caso de uso
/// disparado por requisição HTTP, por isso cada método recebe o Tenant explicitamente em vez de
/// depender de ICurrentTenantContext (que só existe dentro de uma requisição autenticada).
/// </summary>
public class RetentionAlertService
{
    /// <summary>Mesmo limiar usado no relatório de Engajamento (ReportService/EngagementBadge no
    /// frontend) — um aluno "sumido" pros dois lugares é a mesma coisa.</summary>
    private const int InactivityThresholdDays = 14;
    private const int InactivityCooldownDays = 14;

    private const double PointDropThreshold = 0.30; // 30% de queda
    private const long PointDropMinPreviousCheckins = 5; // volume mínimo pra uma % fazer sentido
    private const int PointDropCooldownDays = 7;

    public const string InactivityTemplateName = "aluno_sumido";
    public const string BirthdayTemplateName = "feliz_aniversario";
    public const string PointDropTemplateName = "queda_movimento";
    private const string LanguageCode = "pt_BR";

    private readonly ICheckinRecordRepository _checkinRepository;
    private readonly IStudentRepository _studentRepository;
    private readonly ICheckinPointRepository _checkinPointRepository;
    private readonly ITenantRepository _tenantRepository;
    private readonly IWhatsAppSender _whatsAppSender;
    private readonly ILogger<RetentionAlertService> _logger;

    public RetentionAlertService(
        ICheckinRecordRepository checkinRepository,
        IStudentRepository studentRepository,
        ICheckinPointRepository checkinPointRepository,
        ITenantRepository tenantRepository,
        IWhatsAppSender whatsAppSender,
        ILogger<RetentionAlertService> logger)
    {
        _checkinRepository = checkinRepository;
        _studentRepository = studentRepository;
        _checkinPointRepository = checkinPointRepository;
        _tenantRepository = tenantRepository;
        _whatsAppSender = whatsAppSender;
        _logger = logger;
    }

    /// <summary>Roda os três alertas para todos os tenants com assinatura em dia — chamado uma
    /// vez por dia pelo hosted service. Tenants Inactive/Canceled não recebem (nunca ativaram ou
    /// já cancelaram, não faz sentido gastar mensagem com eles).</summary>
    public async Task RunDailyChecksAsync(CancellationToken ct = default)
    {
        if (!_whatsAppSender.IsConfigured)
        {
            _logger.LogInformation("Régua de relacionamento: WhatsApp:BaseUrl não configurado, execução pulada.");
            return;
        }

        var tenants = (await _tenantRepository.ListAsync(ct))
            .Where(t => t.SubscriptionStatus is SubscriptionStatus.Active or SubscriptionStatus.PastDue)
            .ToList();

        foreach (var tenant in tenants)
        {
            await SendInactivityAlertsAsync(tenant, ct);
            await SendBirthdayAlertsAsync(tenant, ct);
            await SendPointDropAlertsAsync(tenant, ct);
        }
    }

    /// <summary>Manda "sentimos sua falta" pra quem já veio antes mas sumiu — quem nunca fez
    /// check-in nenhum não entra aqui (não é "sumido", é "nunca engajou"; mandar essa mensagem
    /// pra ele não faz sentido).</summary>
    public async Task<int> SendInactivityAlertsAsync(Tenant tenant, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var students = await _studentRepository.ListAsync(tenant.Id, ct);
        var lastCheckinAtByStudent = await _checkinRepository.GetLastCheckinAtByStudentAsync(tenant.Id, ct);

        var sent = 0;
        foreach (var student in students.Where(s => s.Active && !string.IsNullOrWhiteSpace(s.Phone)))
        {
            if (!lastCheckinAtByStudent.TryGetValue(student.Id, out var lastCheckinAt)) continue;

            var daysSince = (now.Date - lastCheckinAt.Date).TotalDays;
            if (daysSince < InactivityThresholdDays) continue;

            if (student.LastInactivityAlertSentAt.HasValue &&
                (now - student.LastInactivityAlertSentAt.Value).TotalDays < InactivityCooldownDays) continue;

            var result = await _whatsAppSender.SendTemplateAsync(
                student.Phone!, InactivityTemplateName, LanguageCode, [student.Name, tenant.Name], ct);

            if (result.Success)
            {
                student.LastInactivityAlertSentAt = now;
                await _studentRepository.UpdateAsync(student, ct);
                sent++;
            }
            else
            {
                _logger.LogWarning("Falha ao mandar alerta de inatividade pro aluno {StudentId}: {Error}", student.Id, result.Error);
            }
        }
        return sent;
    }

    public async Task<int> SendBirthdayAlertsAsync(Tenant tenant, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var students = await _studentRepository.ListAsync(tenant.Id, ct);

        var sent = 0;
        foreach (var student in students.Where(s => s.Active && s.BirthDate.HasValue && !string.IsNullOrWhiteSpace(s.Phone)))
        {
            var birthDate = student.BirthDate!.Value;
            if (birthDate.Month != now.Month || birthDate.Day != now.Day) continue;
            if (student.LastBirthdayAlertSentYear == now.Year) continue;

            var result = await _whatsAppSender.SendTemplateAsync(student.Phone!, BirthdayTemplateName, LanguageCode, [student.Name], ct);

            if (result.Success)
            {
                student.LastBirthdayAlertSentYear = now.Year;
                await _studentRepository.UpdateAsync(student, ct);
                sent++;
            }
            else
            {
                _logger.LogWarning("Falha ao mandar parabéns pro aluno {StudentId}: {Error}", student.Id, result.Error);
            }
        }
        return sent;
    }

    /// <summary>Compara os últimos 7 dias de check-in de cada ponto com os 7 dias anteriores —
    /// avisa <see cref="Tenant.AlertsWhatsAppPhone"/> se caiu <see cref="PointDropThreshold"/> ou
    /// mais. Ignora pontos com pouco volume no período anterior (não dá pra falar de "queda"
    /// quando a base já era pequena — vira ruído).</summary>
    public async Task<int> SendPointDropAlertsAsync(Tenant tenant, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(tenant.AlertsWhatsAppPhone)) return 0;

        var now = DateTime.UtcNow;
        var currentStart = now.AddDays(-7);
        var previousStart = now.AddDays(-14);

        var points = await _checkinPointRepository.ListAsync(tenant.Id, ct);
        var currentByPoint = (await _checkinRepository.ListAsync(tenant.Id, currentStart, now, ct: ct))
            .GroupBy(r => r.CheckinPointId).ToDictionary(g => g.Key, g => g.LongCount());
        var previousByPoint = (await _checkinRepository.ListAsync(tenant.Id, previousStart, currentStart, ct: ct))
            .GroupBy(r => r.CheckinPointId).ToDictionary(g => g.Key, g => g.LongCount());

        var sent = 0;
        foreach (var point in points.Where(p => p.Active))
        {
            previousByPoint.TryGetValue(point.Id, out var previousCount);
            if (previousCount < PointDropMinPreviousCheckins) continue;

            currentByPoint.TryGetValue(point.Id, out var currentCount);
            var dropRatio = (double)(previousCount - currentCount) / previousCount;
            if (dropRatio < PointDropThreshold) continue;

            if (point.LastDropAlertSentAt.HasValue &&
                (now - point.LastDropAlertSentAt.Value).TotalDays < PointDropCooldownDays) continue;

            var dropPercentText = Math.Round(dropRatio * 100).ToString(CultureInfo.InvariantCulture);
            var result = await _whatsAppSender.SendTemplateAsync(
                tenant.AlertsWhatsAppPhone!, PointDropTemplateName, LanguageCode, [point.Name, dropPercentText], ct);

            if (result.Success)
            {
                point.LastDropAlertSentAt = now;
                await _checkinPointRepository.UpdateAsync(point, ct);
                sent++;
            }
            else
            {
                _logger.LogWarning("Falha ao mandar alerta de queda de movimento pro ponto {PointId}: {Error}", point.Id, result.Error);
            }
        }
        return sent;
    }
}
