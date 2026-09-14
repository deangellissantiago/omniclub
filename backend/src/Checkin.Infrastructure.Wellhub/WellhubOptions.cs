namespace Checkin.Infrastructure.Wellhub;

public class WellhubOptions
{
    /// <summary>
    /// Host da Access Control API. Produção: <c>https://api.partners.gympass.com</c> (default
    /// abaixo). Sandbox: <c>https://apitesting.partners.gympass.com</c> — confirmado na prática
    /// em 2026-09-10 fazendo um POST /access/v1/validate real com a api_key de Sandbox (o host de
    /// produção respondia 401 para essa credencial; o de testing respondeu 404 "Check-In not
    /// found", ou seja, autenticou e chegou na regra de negócio). Configurado via
    /// <c>Wellhub:BaseUrl</c> / env <c>WELLHUB_BASE_URL</c> — troque para o de produção quando a
    /// escola virar parceira homologada de verdade.
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.partners.gympass.com";

    /// <summary>
    /// Bearer token usado no header Authorization das chamadas de saída (/access/v1/validate e
    /// /access/v1/code/:wellhub_id). Só é emitido pelo Wellhub Technical Sales depois que a
    /// escola vira parceira/CMS homologado — fica vazio até lá.
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// Chave secreta usada para validar o header X-Gympass-Signature do Check-in Webhook
    /// (HMAC-SHA1 do corpo da requisição, hex, maiúsculo). Também fornecida pelo Wellhub
    /// Technical Sales.
    /// </summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
