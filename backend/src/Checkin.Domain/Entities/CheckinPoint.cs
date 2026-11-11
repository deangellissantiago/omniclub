using Checkin.Domain.Enums;

namespace Checkin.Domain.Entities;

/// <summary>
/// Ponto de check-in (quadra/unidade) cadastrado pelo admin do tenant, vinculado a um app
/// (ex.: o "gym id" do Wellhub, como 500977 = Dynamis Santa Lúcia).
/// </summary>
public class CheckinPoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;
    public IntegrationApp App { get; set; }

    /// <summary>Identificador do ponto no app de origem (ex.: Wellhub GymId "500977").</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Valor (em centavos) que o app de benefício repassa à academia por check-in
    /// aprovado neste ponto — negociado no contrato com o Wellhub/TotalPass, varia por unidade.
    /// Nulo enquanto o admin não configurar (relatório de repasse fica "não configurado" até lá,
    /// ver ReportService.RevenueAsync — nunca estimamos em cima de um valor que não foi informado).</summary>
    public int? PricePerCheckinCents { get; set; }

    /// <summary>Último alerta de queda de movimento mandado pro WhatsApp de operação do tenant
    /// (ver Tenant.AlertsWhatsAppPhone) — evita mandar de novo todo dia enquanto o movimento
    /// continuar baixo (cooldown, ver RetentionAlertService).</summary>
    public DateTime? LastDropAlertSentAt { get; set; }

    /// <summary>Produtos Wellhub vinculados a este ponto (ver CheckinPointService.SyncProductsAsync
    /// e WellhubProductRef). Vazio até alguém clicar em "Buscar produtos" no cadastro do ponto.</summary>
    public List<WellhubProductRef> Products { get; set; } = new();

    // ---- TotalPass ---------------------------------------------------------------------

    /// <summary>Chave da unidade no TotalPass ("place_api_key"), gerada pela própria academia no
    /// sistema do TotalPass e colada aqui — junto com a chave de integrador do OmniClub
    /// (TotalPass:PartnerApiKey) é o que autoriza o OmniClub a operar esta unidade.</summary>
    public string? TotalPassPlaceApiKey { get; set; }

    /// <summary>Segredo aleatório que compõe a URL de webhook desta unidade
    /// (/api/integrations/totalpass/checkins/{chave}). O TotalPass não assina os webhooks (nada
    /// documentado), então é isto que impede terceiros de injetar check-ins: só quem tem a URL
    /// exata — o próprio TotalPass, onde ela foi cadastrada — consegue chamar.</summary>
    public string? TotalPassWebhookKey { get; set; }

    /// <summary>Última vez que a URL de webhook foi cadastrada no TotalPass com sucesso (botão
    /// "Conectar ao TotalPass"). Nulo = nunca conectado.</summary>
    public DateTime? TotalPassConnectedAt { get; set; }

    /// <summary>Nome da unidade como o TotalPass a conhece (resposta do /partner/auth) — só para
    /// conferência no painel.</summary>
    public string? TotalPassPlaceName { get; set; }
}
