import { apiClient } from "./client";
import type { DateRangeFilter, GeneralReport, SchoolReportItem, StudentReportItem } from "./types";

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
