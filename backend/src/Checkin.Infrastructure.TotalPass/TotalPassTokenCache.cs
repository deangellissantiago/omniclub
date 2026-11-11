using System.Collections.Concurrent;

namespace Checkin.Infrastructure.TotalPass;

/// <summary>
/// JWT por unidade (place_api_key) — o TotalPass emite tokens de 24 h e não tem endpoint de
/// renovação, então guardamos e pedimos outro um pouco antes de vencer. Singleton: o adapter
/// HTTP é transiente (typed HttpClient) e perderia o cache a cada uso.
/// </summary>
public class TotalPassTokenCache
{
    /// <summary>Renova com folga de 1 h em relação às 24 h documentadas.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(23);

    private readonly ConcurrentDictionary<string, (string Token, DateTime ExpiresAt)> _tokens = new();

    public string? Get(string placeApiKey) =>
        _tokens.TryGetValue(placeApiKey, out var entry) && entry.ExpiresAt > DateTime.UtcNow ? entry.Token : null;

    public void Set(string placeApiKey, string token) => _tokens[placeApiKey] = (token, DateTime.UtcNow + Lifetime);

    public void Invalidate(string placeApiKey) => _tokens.TryRemove(placeApiKey, out _);
}
