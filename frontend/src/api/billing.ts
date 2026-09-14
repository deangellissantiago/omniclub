import { apiClient } from "./client";
import type { BillingPortalSession, CheckoutSession, SubscriptionStatusInfo } from "./types";

export async function getSubscriptionStatus(): Promise<SubscriptionStatusInfo> {
  const { data } = await apiClient.get<SubscriptionStatusInfo>("/billing/status");
  return data;
}

export async function startCheckout(): Promise<CheckoutSession> {
  const { data } = await apiClient.post<CheckoutSession>("/billing/checkout");
  return data;
}

export async function openBillingPortal(): Promise<BillingPortalSession> {
  const { data } = await apiClient.post<BillingPortalSession>("/billing/portal");
  return data;
}
