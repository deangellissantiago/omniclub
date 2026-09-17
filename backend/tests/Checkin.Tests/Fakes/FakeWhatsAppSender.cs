using Checkin.Application.Ports.Integrations;

namespace Checkin.Tests.Fakes;

public class FakeWhatsAppSender : IWhatsAppSender
{
    public bool IsConfigured { get; set; } = true;
    public bool NextResultSucceeds { get; set; } = true;

    public record SentMessage(string ToPhone, string TemplateName, string LanguageCode, IReadOnlyList<string> Parameters);
    public List<SentMessage> Sent { get; } = new();

    public Task<WhatsAppSendResult> SendTemplateAsync(
        string toPhone, string templateName, string languageCode, IReadOnlyList<string> parameters, CancellationToken ct = default)
    {
        Sent.Add(new SentMessage(toPhone, templateName, languageCode, parameters));
        return Task.FromResult(NextResultSucceeds
            ? new WhatsAppSendResult(true, "wamid.fake", null)
            : new WhatsAppSendResult(false, null, "falha simulada"));
    }
}
