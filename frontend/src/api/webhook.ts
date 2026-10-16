import { apiClient } from "./client";
import type { OutboundWebhookDelivery, OutboundWebhookSettings, OutboundWebhookTestResult } from "./types";

export async function getWebhookSettings(): Promise<OutboundWebhookSettings> {
  const { data } = await apiClient.get<OutboundWebhookSettings>("/settings/webhook");
  return data;
}

/** `secret` omitido = mantém o atual; `url` vazio = desativa o envio. */
export async function updateWebhookSettings(payload: { url: string | null; secret?: string | null }): Promise<OutboundWebhookSettings> {
  const { data } = await apiClient.put<OutboundWebhookSettings>("/settings/webhook", payload);
  return data;
}

export async function regenerateWebhookSecret(): Promise<OutboundWebhookSettings> {
  const { data } = await apiClient.post<OutboundWebhookSettings>("/settings/webhook/secret");
  return data;
}

export async function sendWebhookTest(): Promise<OutboundWebhookTestResult> {
  const { data } = await apiClient.post<OutboundWebhookTestResult>("/settings/webhook/test");
  return data;
}

export async function listWebhookDeliveries(): Promise<OutboundWebhookDelivery[]> {
  const { data } = await apiClient.get<OutboundWebhookDelivery[]>("/settings/webhook/deliveries");
  return data;
}

export async function retryWebhookDelivery(id: string): Promise<OutboundWebhookDelivery> {
  const { data } = await apiClient.post<OutboundWebhookDelivery>(`/settings/webhook/deliveries/${id}/retry`);
  return data;
}
