export type IntegrationApp = "Wellhub" | "TotalPass";
export type CheckinStatus = "Approved" | "Rejected" | "Pending";

export interface LoginRequest {
  email: string;
  password: string;
}

export interface RegisterRequest {
  tenantName: string;
  adminName: string;
  email: string;
  password: string;
}

export interface LoginResponse {
  token: string;
  adminName: string;
  tenantId: string;
  tenantName: string;
}

export type SubscriptionStatus = "Inactive" | "Active" | "PastDue" | "Canceled";

export interface SubscriptionStatusInfo {
  status: SubscriptionStatus;
  currentPeriodEnd?: string | null;
}

export interface CheckoutSession {
  url: string;
}

export interface BillingPortalSession {
  url: string;
}

export interface Student {
  id: string;
  name: string;
  email?: string | null;
  phone?: string | null;
  document?: string | null;
  wellhubMemberId?: string | null;
  totalPassMemberId?: string | null;
  active: boolean;
  createdAt: string;
}

export type UpsertStudent = Omit<Student, "id" | "createdAt">;

export interface WellhubProduct {
  productId: number;
  name: string;
  virtual: boolean;
}

export interface CheckinPoint {
  id: string;
  app: IntegrationApp;
  externalId: string;
  name: string;
  active: boolean;
  createdAt: string;
  products: WellhubProduct[];
}

export type UpsertCheckinPoint = Omit<CheckinPoint, "id" | "createdAt" | "products">;

export interface CheckinRecord {
  id: string;
  studentId?: string | null;
  studentName?: string | null;
  checkinPointId: string;
  checkinPointName?: string | null;
  app: IntegrationApp;
  occurredAt: string;
  status: CheckinStatus;
}

export interface AppCount {
  app: string;
  total: number;
}

export interface DashboardSummary {
  checkinsByApp: AppCount[];
  studentsByApp: AppCount[];
  totalCheckins: number;
  totalStudents: number;
  recentCheckins: CheckinRecord[];
}

export interface StudentReportItem {
  studentId: string;
  studentName: string;
  totalCheckins: number;
  lastCheckinAt?: string | null;
}

export interface SchoolReportItem {
  checkinPointId: string;
  checkinPointName: string;
  app: string;
  totalCheckins: number;
  lastCheckinAt?: string | null;
}

export interface GeneralReport {
  totalCheckins: number;
  checkinsByApp: AppCount[];
  topStudents: StudentReportItem[];
  byCheckinPoint: SchoolReportItem[];
}

export interface DateRangeFilter {
  startDate?: string;
  endDate?: string;
}

export interface StudentEngagement {
  studentId: string;
  studentName: string;
  lastCheckinAt?: string | null;
  /** Nulo quando o aluno nunca fez check-in nenhum. */
  daysSinceLastCheckin?: number | null;
  checkinsLast30Days: number;
  avgCheckinsPerWeek: number;
}

/** Não é filtrável por período — sempre relativo a "agora" (`asOf`), ver ReportService.EngagementAsync no backend. */
export interface EngagementReport {
  students: StudentEngagement[];
  asOf: string;
}

export type WeekDay = "Sunday" | "Monday" | "Tuesday" | "Wednesday" | "Thursday" | "Friday" | "Saturday";

export interface PeakHourCell {
  dayOfWeek: WeekDay;
  hour: number;
  totalCheckins: number;
}

export interface PeakHoursReport {
  cells: PeakHourCell[];
}

export type BookingStatus = "Requested" | "Confirmed" | "Rejected" | "Canceled" | "LateCanceled";

export interface BookingStatusCount {
  status: BookingStatus;
  total: number;
}

export interface AttendanceReport {
  totalBookings: number;
  byStatus: BookingStatusCount[];
  occupancyRate: number;
  noShowRate: number;
}

export interface GrowthPeriod {
  periodStart: string;
  newStudents: number;
}

export interface GrowthReport {
  groupBy: "week" | "month";
  periods: GrowthPeriod[];
}
