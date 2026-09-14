namespace Checkin.Domain.Enums;

/// <summary>
/// Aplicativo de benefício de bem-estar de onde um check-in se origina.
/// Hoje só há integração com o Wellhub; o TotalPass é apenas um valor reservado
/// para quando a integração for implementada, sem exigir mudanças de modelo.
/// </summary>
public enum IntegrationApp
{
    Wellhub = 1,
    TotalPass = 2
}
