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
  /** Valor (em centavos) que o app repassa à academia por check-in aprovado neste ponto.
   * Nulo enquanto o admin não configurar — o relatório de repasse não estima em cima disso. */
  pricePerCheckinCents?: number | null;
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

export interface RevenuePerPoint {
  checkinPointId: string;
  checkinPointName: string;
  app: string;
  approvedCheckins: number;
  pricePerCheckinCents?: number | null;
  /** Nulo quando o ponto não tem valor configurado — nunca é uma estimativa "chutada". */
  estimatedRevenueCents?: number | null;
}

/** Conciliação de repasse Wellhub/TotalPass — o diferencial que só o OmniClub calcula. */
export interface RevenueReport {
  totalApprovedCheckins: number;
  totalEstimatedRevenueCents?: number | null;
  byPoint: RevenuePerPoint[];
  pointsWithoutPriceConfigured: number;
}

export interface AppPenetrationItem {
  app: string;
  activeStudents: number;
  percent: number;
}

/** Não é filtrável por período — sempre "a foto de agora" (mesma filosofia do EngagementReport). */
export interface AppPenetrationReport {
  totalActiveStudents: number;
  items: AppPenetrationItem[];
}

export interface SchoolRankingItem {
  checkinPointId: string;
  checkinPointName: string;
  app: string;
  totalCheckins: number;
  previousPeriodCheckins: number;
  /** Nulo quando o período anterior não teve nenhum check-in ("novo" na UI, não 0%/infinito). */
  changePercent?: number | null;
}

export interface SchoolRankingReport {
  periodStart: string;
  periodEnd: string;
  items: SchoolRankingItem[];
}
