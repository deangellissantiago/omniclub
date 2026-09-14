using Checkin.Domain.Enums;

namespace Checkin.Domain.Entities;

/// <summary>
/// Representa a escola/organização que usa o sistema. Cada Tenant tem seus próprios
/// administradores, alunos, pontos de check-in e histórico de check-ins, totalmente isolados.
/// </summary>
public class Tenant
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Cadastro por autosserviço (AuthService.RegisterAsync) começa <c>Inactive</c> de propósito
    /// — libera o uso do sistema só depois do checkout do Stripe confirmar (ver
    /// SubscriptionGateMiddleware). O default da classe é <c>Active</c>, não <c>Inactive</c>: é
    /// o valor que um tenant "antigo" (criado antes desse campo existir, sem o campo salvo no
    /// Mongo) assume ao ser lido do banco — sem isso, o tenant seedado original ficaria
    /// bloqueado no primeiro deploy desta feature.
    /// </summary>
    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.Active;

    public string? StripeCustomerId { get; set; }
    public string? StripeSubscriptionId { get; set; }
    public DateTime? SubscriptionCurrentPeriodEnd { get; set; }
}
