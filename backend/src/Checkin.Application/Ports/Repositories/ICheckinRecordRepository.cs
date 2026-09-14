using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Application.Ports.Repositories;

public interface ICheckinRecordRepository
{
    Task<CheckinRecord> CreateAsync(CheckinRecord record, CancellationToken ct = default);

    /// <summary>Usado para idempotência: o Check-in Webhook do Wellhub tenta de novo (até 3x) se
    /// não responder em 1s, então o mesmo evento pode chegar mais de uma vez.</summary>
    Task<CheckinRecord?> FindByExternalCheckinIdAsync(string tenantId, string externalCheckinId, CancellationToken ct = default);

    Task<IReadOnlyList<CheckinRecord>> ListRecentAsync(string tenantId, int take, CancellationToken ct = default);

    Task<IReadOnlyList<CheckinRecord>> ListAsync(
        string tenantId, DateTime? start, DateTime? end,
        string? studentId = null, string? checkinPointId = null, CancellationToken ct = default);

    Task<long> CountAsync(string tenantId, DateTime? start, DateTime? end, CancellationToken ct = default);

    Task<long> CountByAppAsync(string tenantId, IntegrationApp app, DateTime? start, DateTime? end, CancellationToken ct = default);

    /// <summary>Data do check-in mais recente de cada aluno (todo o histórico, não só um período) —
    /// usado pelo relatório de engajamento (ver ReportService.EngagementAsync) para achar alunos
    /// "sumidos" sem carregar o histórico inteiro de check-ins na memória.</summary>
    Task<IReadOnlyDictionary<string, DateTime>> GetLastCheckinAtByStudentAsync(string tenantId, CancellationToken ct = default);
}
