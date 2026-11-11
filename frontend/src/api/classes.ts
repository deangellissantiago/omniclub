import { apiClient } from "./client";
import type { ClassSlot, CreateClassRequest, CreateSlotRequest, TotalPassPlan, WellhubClass } from "./types";

export async function listClasses(): Promise<WellhubClass[]> {
  const { data } = await apiClient.get<WellhubClass[]>("/classes");
  return data;
}

/** Cria a aula aqui e nos apps escolhidos (no Wellhub cria a categoria; no TotalPass a publicação
 * acontece horário a horário). */
export async function createClass(payload: CreateClassRequest): Promise<WellhubClass> {
  const { data } = await apiClient.post<WellhubClass>("/classes", payload);
  return data;
}

/** Reenvia aos apps (Wellhub/TotalPass) a aula e os horários futuros que ficaram pendentes. */
export async function syncClass(classId: string): Promise<WellhubClass> {
  const { data } = await apiClient.post<WellhubClass>(`/classes/${classId}/sync`);
  return data;
}

export async function listSlots(classId: string): Promise<ClassSlot[]> {
  const { data } = await apiClient.get<ClassSlot[]>(`/classes/${classId}/slots`);
  return data;
}

export async function createSlot(classId: string, payload: CreateSlotRequest): Promise<ClassSlot> {
  const { data } = await apiClient.post<ClassSlot>(`/classes/${classId}/slots`, payload);
  return data;
}

/** Planos TotalPass da unidade — um deles é obrigatório para publicar a aula no TotalPass. */
export async function listTotalPassPlans(checkinPointId: string): Promise<TotalPassPlan[]> {
  const { data } = await apiClient.get<TotalPassPlan[]>(`/classes/totalpass-plans/${checkinPointId}`);
  return data;
}
