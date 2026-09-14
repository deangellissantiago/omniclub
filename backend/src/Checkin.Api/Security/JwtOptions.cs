namespace Checkin.Api.Security;

public class JwtOptions
{
    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "checkin-api";
    public string Audience { get; set; } = "checkin-clients";
    public int ExpirationMinutes { get; set; } = 480;
}
