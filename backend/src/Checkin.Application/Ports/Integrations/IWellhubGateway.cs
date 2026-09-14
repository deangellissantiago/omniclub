namespace Checkin.Application.Ports.Integrations;

/// <summary>
/// Porta de saída (outbound) para o Wellhub — Access Control API v1.0, confirmada em
/// https://developers.wellhub.com/product/access-control-api/1.0/endpoints
/// (host real: https://api.partners.gympass.com).
///
/// Uso típico: catracas/leitores próprios da escola, quando o aluno apresenta um código
/// (PIN/QR) em vez de fazer o check-in dentro do próprio app Wellhub. Para o cenário descrito
/// pelo cliente (aluno faz check-in pelo app Wellhub e a aprovação deve ser automática), o
/// caminho principal é o Check-in Webhook (ver WellhubWebhookController), não esta porta — ela
/// fica pronta para quando a escola tiver controle de acesso físico nas quadras.
///
/// Credenciais (Authorization: Bearer, X-Gym-Id) só são emitidas pelo Wellhub Technical Sales
/// após a escola virar parceira/CMS homologado — por isso ficam vazias em WellhubOptions até lá.
/// </summary>
public interface IWellhubGateway
{
    /// <summary>
    /// Indica se há credenciais (<c>Wellhub:ApiKey</c>) configuradas para chamar a API de
    /// verdade. Usado pelo caso de uso do Check-in Webhook (ver CheckinService) para decidir
    /// entre chamar <see cref="ValidateAccessAsync"/> de fato ou cair no fallback de aprovação
    /// automática local (só para dev/teste sem credenciais).
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// POST /access/v1/validate — valida a entrada de um usuário na academia (e dispara o
    /// faturamento entre Wellhub e a academia). Retorna null se o Wellhub rejeitar a validação
    /// (400/404) ou se a integração ainda não estiver configurada (ver <see cref="IsConfigured"/>).
    /// </summary>
    Task<WellhubValidationResult?> ValidateAccessAsync(
        string gympassId, string? customCode, string gymExternalId, CancellationToken ct = default);

    /// <summary>POST /access/v1/code/:wellhub_id — cria o código de acesso (PIN/QR) do aluno para esta academia.</summary>
    Task<bool> CreateCustomCodeAsync(string wellhubId, string customCode, string gymExternalId, CancellationToken ct = default);

    /// <summary>PUT /access/v1/code/:wellhub_id — atualiza o código de acesso do aluno.</summary>
    Task<bool> UpdateCustomCodeAsync(string wellhubId, string customCode, string gymExternalId, CancellationToken ct = default);

    /// <summary>DELETE /access/v1/code/:wellhub_id — remove o código de acesso do aluno.</summary>
    Task<bool> DeleteCustomCodeAsync(string wellhubId, string gymExternalId, CancellationToken ct = default);
}

public record WellhubValidationResult(string GympassId, long GymId, long? ProductId, string? ProductDescription, DateTime? ValidatedAt);
