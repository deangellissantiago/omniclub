using Checkin.Infrastructure.Wellhub;
using Xunit;

namespace Checkin.Tests;

/// <summary>
/// HMAC-SHA1(corpo, secret) em hex maiúsculo — algoritmo documentado para o header
/// X-Gympass-Signature (ver WellhubSignature). O vetor de teste abaixo foi calculado de forma
/// independente (openssl dgst -sha1 -hmac) para não repetir a própria implementação.
/// </summary>
public class WellhubSignatureTests
{
    private const string Body = "{\"event_type\":\"checkin\"}";
    private const string Secret = "meu-segredo-de-sandbox";

    // Vetor calculado de forma independente da implementação (fora do .NET), para travar o
    // comportamento de verdade:
    //   printf '%s' '{"event_type":"checkin"}' | openssl dgst -sha1 -hmac "meu-segredo-de-sandbox" -hex
    private const string ExpectedSignature = "D71FDA1FBB93E6BF59F96AC51AB995C1A24B7DEE";

    [Fact]
    public void Compute_matches_independently_calculated_hmac_sha1_hex_vector()
    {
        Assert.Equal(ExpectedSignature, WellhubSignature.Compute(Body, Secret));
    }

    [Fact]
    public void IsValid_accepts_correct_signature_case_insensitively_and_trimmed()
    {
        var signature = WellhubSignature.Compute(Body, Secret);

        Assert.True(WellhubSignature.IsValid(Body, Secret, signature));
        Assert.True(WellhubSignature.IsValid(Body, Secret, " " + signature.ToLowerInvariant() + " "));
    }

    [Fact]
    public void IsValid_rejects_wrong_secret()
    {
        var signature = WellhubSignature.Compute(Body, Secret);

        Assert.False(WellhubSignature.IsValid(Body, "outro-secret", signature));
    }

    [Fact]
    public void IsValid_rejects_tampered_body()
    {
        var signature = WellhubSignature.Compute(Body, Secret);

        Assert.False(WellhubSignature.IsValid(Body + "tampered", Secret, signature));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsValid_rejects_missing_signature(string? signature)
    {
        Assert.False(WellhubSignature.IsValid(Body, Secret, signature));
    }
}
