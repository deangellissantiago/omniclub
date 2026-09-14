import { apiClient } from "./client";
import type { CheckinPoint, UpsertCheckinPoint } from "./types";

export async function listCheckinPoints(): Promise<CheckinPoint[]> {
  const { data } = await apiClient.get<CheckinPoint[]>("/checkin-points");
  return data;
}

export async function createCheckinPoint(payload: UpsertCheckinPoint): Promise<CheckinPoint> {
  const { data } = await apiClient.post<CheckinPoint>("/checkin-points", payload);
  return data;
}

export async function updateCheckinPoint(id: string, payload: UpsertCheckinPoint): Promise<CheckinPoint> {
  const { data } = await apiClient.put<CheckinPoint>(`/checkin-points/${id}`, payload);
  return data;
}

export async function deleteCheckinPoint(id: string): Promise<void> {
  await apiClient.delete(`/checkin-points/${id}`);
}

/** Busca os produtos (planos/tipos de acesso) reais deste ponto no Wellhub e vincula ao
 * cadastro, pra ficarem visíveis (GET /setup/v1/gyms/:gym_id/products por baixo). */
export async function syncCheckinPointProducts(id: string): Promise<CheckinPoint> {
  const { data } = await apiClient.post<CheckinPoint>(`/checkin-points/${id}/sync-products`);
  return data;
}
