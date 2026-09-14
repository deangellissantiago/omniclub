using Checkin.Application.DTOs.Checkins;
using Checkin.Application.Ports;
using Checkin.Application.Ports.Integrations;
using Checkin.Application.Ports.Repositories;
using Checkin.Domain.Entities;
using Checkin.Domain.Enums;

namespace Checkin.Application.UseCases.Checkins;

public class CheckinService
{
    private readonly ICheckinRecordRepository _checkinRepository;
    private readonly ICheckinPointRepository _checkinPointRepository;
    private readonly IStudentRepository _studentRepository;
    private readonly IWellhubGateway _wellhubGateway;
    private readonly ICurrentTenantContext _tenantContext;

    public CheckinService(
        ICheckinRecordRepository checkinRepository,
        ICheckinPointRepository checkinPointRepository,
        IStudentRepository studentRepository,
        IWellhubGateway wellhubGateway,
        ICurrentTenantContext tenantContext)
    {
        _checkinRepository = checkinRepository;
        _checkinPointRepository = checkinPointRepository;
        _studentRepository = studentRepository;
        _wellhubGateway = wellhubGateway;
        _tenantContext = tenantContext;
    }

    public async Task<IReadOnlyList<CheckinDto>> ListAsync(DateTime? start, DateTime? end, string? studentId, string? checkinPointId, CancellationToken ct = default)
    {
        var records = await _checkinRepository.ListAsync(_tenantContext.TenantId, start, end, studentId, checkinPointId, ct);
        var students = await _studentRepository.ListAsync(_tenantContext.TenantId, ct);
        var points = await _checkinPointRepository.ListAsync(_tenantContext.TenantId, ct);

        return records.Select(r => ToDto(r, students, points)).ToList();
    }

    /// <summary>
    /// Ponto de entrada usado pelo Check-in Webhook do Wellhub (ver WellhubWebhookController):
    /// registra o pré-check-in notificado pelo webhook e então chama a Access Control API
    /// (<c>POST /access/v1/validate</c>, via IWellhubGateway) para confirmar que o usuário tem
    /// um passe válido para o dia, conforme o fluxo descrito pelo Wellhub Technical Sales —
    /// "seu sistema chama o endpoint /validate ... com a resposta positiva, libera o acesso".
    /// Aprovação automática só ocorre quando a validação retorna sucesso; se o Wellhub rejeitar
    /// (400/404) o check-in é gravado como Rejected. Sem <c>Wellhub:ApiKey</c> configurado (dev
    /// local sem credenciais), cai no fallback de aprovar direto, como antes.
    ///
    /// Se não existe aluno cadastrado com esse Wellhub ID e o webhook trouxe um nome
    /// (<paramref name="userInfo"/>), pré-registra o aluno automaticamente com os dados que o
    /// próprio Wellhub mandou (nome, e-mail, telefone) — conforme o e-mail do Wellhub Technical
    /// Sales ("que pode optar por fazer um pré-registro do usuário") — e já vincula o check-in a
    /// ele. Sem nome (ex.: chamada de teste via /api/simulate sem esses campos), mantém o
    /// check-in como "Não identificado", igual antes.
    /// </summary>
    public async Task<CheckinDto> RegisterWellhubCheckinAsync(
        string gymExternalId, string memberExternalRef, string? externalCheckinId,
        DateTime occurredAt, string? rawPayload, WellhubUserInfo? userInfo = null, CancellationToken ct = default)
    {
        var checkinPoint = await _checkinPointRepository.GetByExternalIdAsync(IntegrationApp.Wellhub, gymExternalId, ct)
            ?? throw new Exceptions.NotFoundException($"Nenhum ponto de check-in Wellhub cadastrado para o id '{gymExternalId}'.");

        if (!string.IsNullOrWhiteSpace(externalCheckinId))
        {
            var existing = await _checkinRepository.FindByExternalCheckinIdAsync(checkinPoint.TenantId, externalCheckinId, ct);
            if (existing is not null)
            {
                var existingStudent = existing.StudentId is not null ? await _studentRepository.GetByIdAsync(checkinPoint.TenantId, existing.StudentId, ct) : null;
                return ToDto(existing, existingStudent is null ? Array.Empty<Student>() : new[] { existingStudent }, new[] { checkinPoint });
            }
        }

        var student = await _studentRepository.GetByWellhubMemberIdAsync(memberExternalRef, ct);
        if (student is null && !string.IsNullOrWhiteSpace(userInfo?.FirstName))
        {
            var name = string.Join(" ", new[] { userInfo.FirstName, userInfo.LastName }.Where(s => !string.IsNullOrWhiteSpace(s)));
            student = await _studentRepository.CreateAsync(new Student
            {
                TenantId = checkinPoint.TenantId,
                Name = name,
                Email = userInfo.Email,
                Phone = userInfo.PhoneNumber,
                WellhubMemberId = memberExternalRef
            }, ct);
        }

        CheckinStatus status;
        DateTime? approvedAt;
        if (_wellhubGateway.IsConfigured)
        {
            var validation = await _wellhubGateway.ValidateAccessAsync(memberExternalRef, customCode: null, checkinPoint.ExternalId, ct);
            status = validation is not null ? CheckinStatus.Approved : CheckinStatus.Rejected;
            approvedAt = validation is not null ? DateTime.UtcNow : null;
        }
        else
        {
            // Sem Wellhub:ApiKey configurado: mantém aprovação automática local (dev/teste sem
            // credenciais de sandbox).
            status = CheckinStatus.Approved;
            approvedAt = DateTime.UtcNow;
        }

        var record = new CheckinRecord
        {
            TenantId = checkinPoint.TenantId,
            StudentId = student?.Id,
            CheckinPointId = checkinPoint.Id,
            App = IntegrationApp.Wellhub,
            ExternalCheckinId = externalCheckinId,
            ExternalMemberRef = memberExternalRef,
            OccurredAt = occurredAt,
            Status = status,
            ApprovedAt = approvedAt,
            RawPayload = rawPayload
        };

        var created = await _checkinRepository.CreateAsync(record, ct);
        return ToDto(created, student is null ? Array.Empty<Student>() : new[] { student }, new[] { checkinPoint });
    }

    private static CheckinDto ToDto(CheckinRecord r, IReadOnlyList<Student> students, IReadOnlyList<CheckinPoint> points)
    {
        var student = r.StudentId is not null ? students.FirstOrDefault(s => s.Id == r.StudentId) : null;
        var point = points.FirstOrDefault(p => p.Id == r.CheckinPointId);
        return new CheckinDto(r.Id, r.StudentId, student?.Name, r.CheckinPointId, point?.Name, r.App, r.OccurredAt, r.Status);
    }
}
