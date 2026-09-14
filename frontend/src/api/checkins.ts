import { apiClient } from "./client";
import type { CheckinRecord, DateRangeFilter } from "./types";

export async function listCheckins(filter: DateRangeFilter & { studentId?: string; checkinPointId?: string }): Promise<CheckinRecord[]> {
  const { data } = await apiClient.get<CheckinRecord[]>("/checkins", { params: filter });
  return data;
}
