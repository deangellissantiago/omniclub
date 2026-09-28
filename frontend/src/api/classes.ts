import { apiClient } from "./client";
import type { ClassSlot, CreateClassRequest, CreateSlotRequest, WellhubClass } from "./types";

export async function listClasses(): Promise<WellhubClass[]> {
  const { data } = await apiClient.get<WellhubClass[]>("/classes");
  return data;
}

/** Cria a categoria aqui e no Wellhub (POST /booking/v1/gyms/:gym_id/classes por baixo). */
export async function createClass(payload: CreateClassRequest): Promise<WellhubClass> {
  const { data } = await apiClient.post<WellhubClass>("/classes", payload);
  return data;
}

/** Reenvia ao Wellhub a categoria e os horários futuros que ficaram pendentes. */
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
