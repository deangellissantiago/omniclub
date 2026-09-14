using Checkin.Domain.Enums;

namespace Checkin.Domain.Entities;

/// <summary>
/// Um check-in realizado por um aluno em um ponto de check-in, através de um dos apps.
/// A aprovação é sempre automática (ver CheckinService), não existe fluxo de aprovação manual.
/// </summary>
public class CheckinRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Nulo quando o check-in não pôde ser casado com nenhum aluno cadastrado.</summary>
    public string? StudentId { get; set; }

    public string CheckinPointId { get; set; } = string.Empty;
    public IntegrationApp App { get; set; }

    /// <summary>Id do check-in no sistema de origem, quando disponível (auditoria/idempotência).</summary>
    public string? ExternalCheckinId { get; set; }

    /// <summary>Identificador do membro enviado pelo app de origem (ex.: Wellhub ID).</summary>
    public string? ExternalMemberRef { get; set; }

    public DateTime OccurredAt { get; set; }
    public CheckinStatus Status { get; set; } = CheckinStatus.Approved;
    public DateTime? ApprovedAt { get; set; }

    /// <summary>Payload bruto recebido do app de origem, para auditoria/depuração.</summary>
    public string? RawPayload { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
