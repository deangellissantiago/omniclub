using Checkin.Domain.Enums;

namespace Checkin.Application.DTOs.Billing;

public record CheckoutSessionDto(string Url);

public record BillingPortalSessionDto(string Url);

public record SubscriptionStatusDto(SubscriptionStatus Status, DateTime? CurrentPeriodEnd);
