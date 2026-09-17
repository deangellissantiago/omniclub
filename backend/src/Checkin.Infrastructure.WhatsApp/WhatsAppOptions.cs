namespace Checkin.Infrastructure.WhatsApp;

/// <summary>Como alcançar o serviço standalone `whatsapp-service` (repositório separado, Meta
/// WhatsApp Cloud API por trás) — não são credenciais da Meta em si, essas ficam configuradas
/// no próprio whatsapp-service.</summary>
public class WhatsAppOptions
{
    /// <summary>Ex.: <c>http://whatsapp-service:8080/api</c> (rede docker compartilhada) em
    /// produção, ou <c>http://localhost:5290/api</c> rodando local sem Docker. Vazio = régua de
    /// relacionamento desligada (RetentionAlertHostedService não chama nada).</summary>
    public string BaseUrl { get; set; } = string.Empty;

    /// <summary>Vai no header X-Api-Key — precisa ser o mesmo valor de ServiceApiKey configurado
    /// no whatsapp-service.</summary>
    public string ApiKey { get; set; } = string.Empty;
}
