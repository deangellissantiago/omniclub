using System.Threading.Channels;

namespace Checkin.Api.BackgroundJobs;

/// <summary>
/// Acorda o WebhookProcessingHostedService assim que um evento novo é gravado, em vez de
/// depender só do polling — é o que dá a latência baixa sem precisar de Redis/fila externa (ver
/// WebhookEvent, comentário "Por quê"). Singleton: o controller (por request) escreve, o hosted
/// service (singleton de longa duração) lê.
/// </summary>
public interface IWebhookEventSignal
{
    void Notify();
    Task WaitAsync(CancellationToken ct);
}

public class ChannelWebhookEventSignal : IWebhookEventSignal
{
    // Capacidade 1 + DropWrite: só importa "tem algo novo, acorda" — não precisa empilhar sinais
    // (o worker já drena a fila inteira a cada acordada, ver ProcessPendingBatchAsync em loop).
    private readonly Channel<byte> _channel = Channel.CreateBounded<byte>(
        new BoundedChannelOptions(1) { FullMode = BoundedChannelFullMode.DropWrite });

    public void Notify() => _channel.Writer.TryWrite(0);

    public async Task WaitAsync(CancellationToken ct)
    {
        await _channel.Reader.WaitToReadAsync(ct);
        while (_channel.Reader.TryRead(out _))
        {
            // drena qualquer sinal acumulado
        }
    }
}
