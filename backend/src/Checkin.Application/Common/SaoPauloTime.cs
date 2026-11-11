namespace Checkin.Application.Common;

/// <summary>Conversão UTC ↔ horário de São Paulo — as APIs dos apps (e o webhook de saída)
/// trabalham com o horário local da unidade.</summary>
public static class SaoPauloTime
{
    public static readonly TimeZoneInfo Zone = Find();

    /// <summary>Brasil não tem horário de verão desde 2019 — se a imagem não tiver a base de
    /// fusos, o fixo -03:00 dá o mesmo resultado.</summary>
    private static TimeZoneInfo Find()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"); }
        catch (Exception) { return TimeZoneInfo.CreateCustomTimeZone("BRT", TimeSpan.FromHours(-3), "BRT", "BRT"); }
    }

    public static DateTimeOffset FromUtc(DateTime utc)
    {
        var asUtc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);
        return TimeZoneInfo.ConvertTime(new DateTimeOffset(asUtc), Zone);
    }
}
