namespace Checkin.Domain.Enums;

/// <summary>
/// Status da assinatura mensal do tenant (Stripe). Um tenant recém-cadastrado começa
/// <see cref="Inactive"/> — todas as rotas autenticadas ficam bloqueadas (exceto login e
/// cobrança) até o checkout do Stripe confirmar o pagamento (ver README, "Cobrança / Stripe").
/// </summary>
public enum SubscriptionStatus
{
    /// <summary>Sem assinatura paga ainda, ou assinatura cancelada/vencida sem renovar.</summary>
    Inactive = 1,
    Active = 2,
    /// <summary>Pagamento falhou mas o Stripe ainda está tentando cobrar de novo (retry automático).</summary>
    PastDue = 3,
    Canceled = 4
}
