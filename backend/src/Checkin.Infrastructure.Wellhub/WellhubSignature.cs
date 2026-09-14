using System.Security.Cryptography;
using System.Text;

namespace Checkin.Infrastructure.Wellhub;

/// <summary>
/// Cálculo/validação do header <c>X-Gympass-Signature</c> enviado pelo Wellhub em todo webhook
/// (Check-in, Booking e Integration Setup APIs — ver README, "Requisitos de Segurança &amp;
/// Webhooks"): HMAC-SHA1 do corpo cru da requisição, usando o Secret combinado com o Wellhub
/// Technical Sales, representado em hex maiúsculo.
///
/// Extraído do controller para ser testável isoladamente (<see cref="Compute"/> também é usado
/// pelas ferramentas de simulação local em <c>backend/scripts</c>) e reutilizável pelos futuros
/// webhooks de Booking API / Integration Setup API, que devem compartilhar a mesma URL e Secret
/// (ver e-mail do Wellhub Technical Sales, item "URL Única").
/// </summary>
public static class WellhubSignature
{
    /// <summary>HMAC-SHA1(<paramref name="rawBody"/>, <paramref name="secret"/>), hex maiúsculo.</summary>
    public static string Compute(string rawBody, string secret)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secret);
        var bodyBytes = Encoding.UTF8.GetBytes(rawBody);
        var hash = HMACSHA1.HashData(keyBytes, bodyBytes);
        return Convert.ToHexString(hash); // Convert.ToHexString já retorna maiúsculo
    }

    /// <summary>
    /// Compara o header recebido com a assinatura esperada em tempo constante (evita timing
    /// attack). Aceita a assinatura recebida com/sem espaços e case-insensitive, já que o Wellhub
    /// não documenta formalmente o casing exato do header.
    /// </summary>
    public static bool IsValid(string rawBody, string secret, string? receivedSignature)
    {
        if (string.IsNullOrWhiteSpace(receivedSignature)) return false;

        var expected = Compute(rawBody, secret);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(receivedSignature.Trim().ToUpperInvariant()));
    }
}
