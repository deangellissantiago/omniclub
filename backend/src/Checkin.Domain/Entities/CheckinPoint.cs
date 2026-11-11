using Checkin.Domain.Enums;

namespace Checkin.Domain.Entities;

/// <summary>
/// Ponto de check-in = a unidade física (quadra/academia) cadastrada pelo admin do tenant. Uma
/// mesma unidade pode aceitar Wellhub, TotalPass ou os dois — cada integração é opcional e
/// independente (Wellhub: <see cref="ExternalId"/>; TotalPass: <see cref="TotalPassPlaceApiKey"/>),
/// com valor de repasse próprio. Qual app originou cada check-in fica em CheckinRecord.App.
///
/// (Até 2026-11 cada ponto tinha um único app, no campo "App" — pontos antigos do TotalPass são
/// convertidos na subida por MongoMigrations.)
/// </summary>
public class CheckinPoint
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gym ID da unidade no Wellhub (ex.: "500977"). Vazio = a unidade não aceita Wellhub.
    /// (Nome mantido por compatibilidade com os dados já gravados.)</summary>
    public string ExternalId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public bool Active { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>Valor (em centavos) que o <b>Wellhub</b> repassa à academia por check-in aprovado
    /// nesta unidade — negociado no contrato, varia por unidade. Nulo enquanto o admin não
    /// configurar (relatório de repasse fica "não configurado" até lá, ver
    /// ReportService.RevenueAsync — nunca estimamos em cima de um valor que não foi informado).
    /// O do TotalPass é <see cref="TotalPassPricePerCheckinCents"/>.</summary>
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

    /// <summary>Id da unidade no TotalPass (preenchido na conexão).</summary>
    public string? TotalPassPlaceId { get; set; }

    /// <summary>Valor (em centavos) que o TotalPass repassa por check-in aprovado nesta unidade —
    /// mesma regra de <see cref="PricePerCheckinCents"/>.</summary>
    public int? TotalPassPricePerCheckinCents { get; set; }

    // Métodos (não propriedades) de propósito: não viram campos no Mongo.

    public bool AcceptsWellhub() => !string.IsNullOrWhiteSpace(ExternalId);

    public bool AcceptsTotalPass() => !string.IsNullOrWhiteSpace(TotalPassPlaceApiKey);

    public IReadOnlyList<IntegrationApp> Apps()
    {
        var apps = new List<IntegrationApp>(2);
        if (AcceptsWellhub()) apps.Add(IntegrationApp.Wellhub);
        if (AcceptsTotalPass()) apps.Add(IntegrationApp.TotalPass);
        return apps;
    }

    /// <summary>Valor de repasse por check-in do app informado.</summary>
    public int? PriceFor(IntegrationApp app) => app == IntegrationApp.TotalPass ? TotalPassPricePerCheckinCents : PricePerCheckinCents;

    /// <summary>Identificador da unidade no app informado (Gym ID Wellhub / id TotalPass).</summary>
    public string? ExternalIdFor(IntegrationApp app) =>
        app == IntegrationApp.TotalPass ? TotalPassPlaceId : string.IsNullOrWhiteSpace(ExternalId) ? null : ExternalId;
}
