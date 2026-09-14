import { apiClient } from "./client";
import type {
  AttendanceReport,
  DateRangeFilter,
  EngagementReport,
  GeneralReport,
  GrowthReport,
  PeakHoursReport,
  SchoolReportItem,
  StudentReportItem,
} from "./types";

export async function getReportByStudent(filter: DateRangeFilter & { studentId?: string }): Promise<StudentReportItem[]> {
  const { data } = await apiClient.get<StudentReportItem[]>("/reports/by-student", { params: filter });
  return data;
}

export async function getReportBySchool(filter: DateRangeFilter & { checkinPointId?: string }): Promise<SchoolReportItem[]> {
  const { data } = await apiClient.get<SchoolReportItem[]>("/reports/by-school", { params: filter });
  return data;
}

export async function getGeneralReport(filter: DateRangeFilter): Promise<GeneralReport> {
  const { data } = await apiClient.get<GeneralReport>("/reports/general", { params: filter });
  return data;
}

/** Não aceita filtro de data — é sempre "a situação agora" (ver EngagementReport). */
export async function getEngagementReport(): Promise<EngagementReport> {
  const { data } = await apiClient.get<EngagementReport>("/reports/engagement");
  return data;
}

export async function getPeakHoursReport(filter: DateRangeFilter): Promise<PeakHoursReport> {
  const { data } = await apiClient.get<PeakHoursReport>("/reports/peak-hours", { params: filter });
  return data;
}

export async function getAttendanceReport(filter: DateRangeFilter): Promise<AttendanceReport> {
  const { data } = await apiClient.get<AttendanceReport>("/reports/attendance", { params: filter });
  return data;
}

export async function getGrowthReport(filter: DateRangeFilter & { groupBy?: "week" | "month" }): Promise<GrowthReport> {
  const { data } = await apiClient.get<GrowthReport>("/reports/growth", { params: filter });
  return data;
}

/** Baixa um relatório em CSV (endpoints .../export) e dispara o download no navegador. */
export async function downloadReportCsv(path: string, params: Record<string, string | undefined>, filename: string): Promise<void> {
  const { data } = await apiClient.get<Blob>(path, { params, responseType: "blob" });
  const url = URL.createObjectURL(data);
  const link = document.createElement("a");
  link.href = url;
  link.download = filename;
  link.click();
  URL.revokeObjectURL(url);
}
