namespace Checkin.Application.Ports.Integrations;

public record WhatsAppSendResult(bool Success, string? ProviderMessageId, string? Error);

/// <summary>Porta de saída para mandar WhatsApp — implementada chamando o serviço standalone
/// `whatsapp-service` (repositório separado, Meta WhatsApp Cloud API por trás; ver seu README
/// para como configurar a conta Meta e criar os templates). Só manda mensagens de
/// <b>template</b> pré-aprovado — a Cloud API não aceita texto livre para mensagem iniciada pelo
/// negócio (fora da janela de 24h de atendimento), que é sempre o caso dos alertas automáticos
/// da régua de relacionamento (ver RetentionAlertService).</summary>
public interface IWhatsAppSender
{
    bool IsConfigured { get; }

    Task<WhatsAppSendResult> SendTemplateAsync(
        string toPhone, string templateName, string languageCode, IReadOnlyList<string> parameters, CancellationToken ct = default);
}
