import { Fragment, useEffect, useState } from "react";
import { Link, useSearchParams } from "react-router-dom";
import {
  downloadReportCsv,
  getAppPenetrationReport,
  getAttendanceReport,
  getEngagementReport,
  getGeneralReport,
  getGrowthReport,
  getPeakHoursReport,
  getReportByStudent,
  getReportBySchool,
  getRevenueReport,
  getSchoolRankingReport,
} from "../api/reports";
import type {
  AppPenetrationReport,
  AttendanceReport,
  BookingStatus,
  EngagementReport,
  GeneralReport,
  GrowthReport,
  PeakHourCell,
  PeakHoursReport,
  RevenueReport,
  SchoolRankingReport,
  SchoolReportItem,
  StudentReportItem,
  WeekDay,
} from "../api/types";
import { extractErrorMessage } from "../api/client";
import { formatMoneyCents } from "../lib/money";
import { DateRangeFilter } from "../components/DateRangeFilter";
import { AppBadge, Avatar, EmptyState, EngagementBadge, IconCheckCircle, IconCreditCard, IconDownload, IconMapPin, TableEmpty } from "../components/ui";

type Tab = "general" | "student" | "school" | "engagement" | "peak-hours" | "attendance" | "growth" | "revenue";

const TABS: { key: Tab; label: string }[] = [
  { key: "general", label: "Geral" },
  { key: "student", label: "Por aluno" },
  { key: "school", label: "Por escola" },
  { key: "engagement", label: "Engajamento" },
  { key: "peak-hours", label: "Horários de pico" },
  { key: "attendance", label: "Aulas" },
  { key: "growth", label: "Crescimento" },
  { key: "revenue", label: "Repasse" },
];

const WEEKDAYS: { key: WeekDay; label: string }[] = [
  { key: "Monday", label: "Seg" },
  { key: "Tuesday", label: "Ter" },
  { key: "Wednesday", label: "Qua" },
  { key: "Thursday", label: "Qui" },
  { key: "Friday", label: "Sex" },
  { key: "Saturday", label: "Sáb" },
  { key: "Sunday", label: "Dom" },
];

const BOOKING_STATUS_LABEL: Record<BookingStatus, string> = {
  Requested: "Solicitada",
  Confirmed: "Confirmada",
  Rejected: "Rejeitada (sem vaga)",
  Canceled: "Cancelada",
  LateCanceled: "Cancelada em cima da hora",
};

function formatDate(value?: string | null): string {
  return value ? new Date(value).toLocaleString("pt-BR") : "-";
}

function formatPercent(rate: number): string {
  return `${Math.round(rate * 100)}%`;
}

export function ReportsPage() {
  const [searchParams, setSearchParams] = useSearchParams();
  const tabParam = searchParams.get("tab") as Tab | null;
  const [tab, setTabState] = useState<Tab>(tabParam && TABS.some((t) => t.key === tabParam) ? tabParam : "general");
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [error, setError] = useState<string | null>(null);

  const [general, setGeneral] = useState<GeneralReport | null>(null);
  const [byStudent, setByStudent] = useState<StudentReportItem[]>([]);
  const [bySchool, setBySchool] = useState<SchoolReportItem[]>([]);
  const [engagement, setEngagement] = useState<EngagementReport | null>(null);
  const [peakHours, setPeakHours] = useState<PeakHoursReport | null>(null);
  const [attendance, setAttendance] = useState<AttendanceReport | null>(null);
  const [growth, setGrowth] = useState<GrowthReport | null>(null);
  const [revenue, setRevenue] = useState<RevenueReport | null>(null);
  const [appPenetration, setAppPenetration] = useState<AppPenetrationReport | null>(null);
  const [schoolRanking, setSchoolRanking] = useState<SchoolRankingReport | null>(null);

  const filter = { startDate: startDate || undefined, endDate: endDate || undefined };

  function setTab(next: Tab) {
    setTabState(next);
    setSearchParams(next === "general" ? {} : { tab: next });
  }

  useEffect(() => {
    setError(null);
    const load =
      tab === "general" ? getGeneralReport(filter).then(setGeneral)
      : tab === "student" ? getReportByStudent(filter).then(setByStudent)
      : tab === "school" ? getReportBySchool(filter).then(setBySchool)
      : tab === "engagement" ? getEngagementReport().then(setEngagement)
      : tab === "peak-hours" ? getPeakHoursReport(filter).then(setPeakHours)
      : tab === "attendance" ? getAttendanceReport(filter).then(setAttendance)
      : tab === "growth" ? getGrowthReport(filter).then(setGrowth)
      : Promise.all([
          getRevenueReport(filter).then(setRevenue),
          getAppPenetrationReport().then(setAppPenetration),
          getSchoolRankingReport(filter).then(setSchoolRanking),
        ]);

    load.catch((err) => setError(extractErrorMessage(err)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, startDate, endDate]);

  const sumidos = engagement?.students.filter((s) => s.daysSinceLastCheckin == null || s.daysSinceLastCheckin >= 14) ?? [];

  return (
    <div>
      <div className="page-header">
        <h1>Relatórios</h1>
        <p className="muted">Acompanhe o desempenho do clube por aluno, por unidade ou na visão geral.</p>
      </div>

      <div className="tabs">
        {TABS.map((t) => (
          <button key={t.key} className={`tab ${tab === t.key ? "active" : ""}`} onClick={() => setTab(t.key)}>
            {t.label}
          </button>
        ))}
      </div>

      {tab !== "engagement" && (
        <DateRangeFilter startDate={startDate} endDate={endDate} onChange={(s, e) => { setStartDate(s); setEndDate(e); }} />
      )}

      {error && <p className="error-text">{error}</p>}

      {tab === "general" && general && (
        <>
          <section className="cards-row">
            <div className="stat-card">
              <span className="stat-icon tint-blue"><IconCheckCircle size={20} /></span>
              <div className="stat-body">
                <span className="stat-label">Check-ins no período</span>
                <span className="stat-value">{general.totalCheckins}</span>
              </div>
            </div>
            {general.checkinsByApp.map((a) => (
              <div className="stat-card" key={a.app}>
                <span className={`stat-icon ${a.app === "TotalPass" ? "tint-pink" : "tint-sky"}`}><IconMapPin size={20} /></span>
                <div className="stat-body">
                  <span className="stat-label">Via {a.app}</span>
                  <span className="stat-value">{a.total}</span>
                </div>
              </div>
            ))}
          </section>

          <section className="panel panel-table">
            <h2>Top alunos</h2>
            <table className="data-table">
              <thead><tr><th>Aluno</th><th>Total</th><th>Último check-in</th></tr></thead>
              <tbody>
                {general.topStudents.length === 0 && (
                  <TableEmpty colSpan={3} title="Sem dados no período" hint="Ajuste o filtro de datas para ver resultados." />
                )}
                {general.topStudents.map((s) => (
                  <tr key={s.studentId}>
                    <td>
                      <span className="cell-person">
                        <Avatar name={s.studentName} small />
                        <span className="cell-strong">{s.studentName}</span>
                      </span>
                    </td>
                    <td>{s.totalCheckins}</td>
                    <td>{formatDate(s.lastCheckinAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>

          <section className="panel panel-table">
            <h2>Por ponto de check-in</h2>
            <table className="data-table">
              <thead><tr><th>Ponto</th><th>App</th><th>Total</th><th>Último check-in</th></tr></thead>
              <tbody>
                {general.byCheckinPoint.length === 0 && (
                  <TableEmpty colSpan={4} title="Sem dados no período" hint="Ajuste o filtro de datas para ver resultados." />
                )}
                {general.byCheckinPoint.map((p) => (
                  <tr key={p.checkinPointId}>
                    <td className="cell-strong">{p.checkinPointName}</td>
                    <td><AppBadge app={p.app} /></td>
                    <td>{p.totalCheckins}</td>
                    <td>{formatDate(p.lastCheckinAt)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        </>
      )}

      {tab === "student" && (
        <section className="panel panel-table">
          <div className="panel-toolbar">
            <h2>Check-ins por aluno</h2>
            <button className="btn-ghost" onClick={() => downloadReportCsv("/reports/by-student/export", filter, "relatorio-por-aluno.csv")}>
              <IconDownload size={16} /> Exportar CSV
            </button>
          </div>
          <table className="data-table">
            <thead><tr><th>Aluno</th><th>Total de check-ins</th><th>Último check-in</th></tr></thead>
            <tbody>
              {byStudent.length === 0 && (
                <TableEmpty colSpan={3} title="Sem dados no período" hint="Ajuste o filtro de datas para ver resultados." />
              )}
              {byStudent.map((s) => (
                <tr key={s.studentId}>
                  <td>
                    <span className="cell-person">
                      <Avatar name={s.studentName} small />
                      <span className="cell-strong">{s.studentName}</span>
                    </span>
                  </td>
                  <td>{s.totalCheckins}</td>
                  <td>{formatDate(s.lastCheckinAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {tab === "school" && (
        <section className="panel panel-table">
          <div className="panel-toolbar">
            <h2>Check-ins por escola/unidade</h2>
            <button className="btn-ghost" onClick={() => downloadReportCsv("/reports/by-school/export", filter, "relatorio-por-escola.csv")}>
              <IconDownload size={16} /> Exportar CSV
            </button>
          </div>
          <table className="data-table">
            <thead><tr><th>Ponto</th><th>App</th><th>Total de check-ins</th><th>Último check-in</th></tr></thead>
            <tbody>
              {bySchool.length === 0 && (
                <TableEmpty colSpan={4} title="Sem dados no período" hint="Ajuste o filtro de datas para ver resultados." />
              )}
              {bySchool.map((p) => (
                <tr key={p.checkinPointId}>
                  <td className="cell-strong">{p.checkinPointName}</td>
                  <td><AppBadge app={p.app} /></td>
                  <td>{p.totalCheckins}</td>
                  <td>{formatDate(p.lastCheckinAt)}</td>
                </tr>
              ))}
            </tbody>
          </table>
        </section>
      )}

      {tab === "engagement" && engagement && (
        <>
          <section className="cards-row">
            <div className="stat-card">
              <span className="stat-icon tint-purple"><IconCheckCircle size={20} /></span>
              <div className="stat-body">
                <span className="stat-label">Alunos ativos</span>
                <span className="stat-value">{engagement.students.length}</span>
              </div>
            </div>
            <div className="stat-card">
              <span className="stat-icon tint-lemon"><IconCheckCircle size={20} /></span>
              <div className="stat-body">
                <span className="stat-label">Sem aparecer há 14+ dias</span>
                <span className="stat-value">{sumidos.length}</span>
              </div>
            </div>
          </section>

          <section className="panel panel-table">
            <div className="panel-toolbar">
              <h2>Situação de frequência por aluno</h2>
              <button className="btn-ghost" onClick={() => downloadReportCsv("/reports/engagement/export", {}, "relatorio-engajamento.csv")}>
                <IconDownload size={16} /> Exportar CSV
              </button>
            </div>
            <table className="data-table">
              <thead>
                <tr>
                  <th>Aluno</th><th>Situação</th><th>Último check-in</th>
                  <th>Check-ins (30 dias)</th><th>Frequência/semana</th>
                </tr>
              </thead>
              <tbody>
                {engagement.students.length === 0 && (
                  <TableEmpty colSpan={5} title="Nenhum aluno ativo cadastrado" />
                )}
                {engagement.students.map((s) => (
                  <tr key={s.studentId}>
                    <td>
                      <span className="cell-person">
                        <Avatar name={s.studentName} small />
                        <span className="cell-strong">{s.studentName}</span>
                      </span>
                    </td>
                    <td><EngagementBadge daysSinceLastCheckin={s.daysSinceLastCheckin} /></td>
                    <td>{formatDate(s.lastCheckinAt)}</td>
                    <td>{s.checkinsLast30Days}</td>
                    <td>{s.avgCheckinsPerWeek.toLocaleString("pt-BR", { maximumFractionDigits: 1 })}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        </>
      )}

      {tab === "peak-hours" && peakHours && (
        <section className="panel">
          <h2>Horário de pico</h2>
          <p className="muted">Quantidade de check-ins por dia da semana e hora — ajuda a dimensionar equipe e quadra.</p>
          <PeakHoursHeatmap cells={peakHours.cells} />
        </section>
      )}

      {tab === "attendance" && attendance && (
        <>
          <section className="cards-row">
            <div className="stat-card">
              <span className="stat-icon tint-blue"><IconCheckCircle size={20} /></span>
              <div className="stat-body">
                <span className="stat-label">Reservas no período</span>
                <span className="stat-value">{attendance.totalBookings}</span>
              </div>
            </div>
            <div className="stat-card">
              <span className="stat-icon tint-purple"><IconCheckCircle size={20} /></span>
              <div className="stat-body">
                <span className="stat-label">Taxa de ocupação</span>
                <span className="stat-value">{formatPercent(attendance.occupancyRate)}</span>
              </div>
            </div>
            <div className="stat-card">
              <span className="stat-icon tint-lemon"><IconCheckCircle size={20} /></span>
              <div className="stat-body">
                <span className="stat-label">Taxa de no-show</span>
                <span className="stat-value">{formatPercent(attendance.noShowRate)}</span>
              </div>
            </div>
          </section>

          <section className="panel panel-table">
            <h2>Por status</h2>
            <table className="data-table">
              <thead><tr><th>Status</th><th>Total</th></tr></thead>
              <tbody>
                {attendance.byStatus.length === 0 && <TableEmpty colSpan={2} title="Sem reservas no período" />}
                {attendance.byStatus.map((s) => (
                  <tr key={s.status}>
                    <td className="cell-strong">{BOOKING_STATUS_LABEL[s.status] ?? s.status}</td>
                    <td>{s.total}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        </>
      )}

      {tab === "growth" && growth && (
        <section className="panel">
          <h2>Crescimento da base de alunos</h2>
          <p className="muted">Novos alunos cadastrados por {growth.groupBy === "month" ? "mês" : "semana"}.</p>
          <GrowthChart periods={growth.periods} groupBy={growth.groupBy} />
        </section>
      )}

      {tab === "revenue" && revenue && (
        <>
          <section className="cards-row">
            <div className="stat-card">
              <span className="stat-icon tint-blue"><IconCheckCircle size={20} /></span>
              <div className="stat-body">
                <span className="stat-label">Check-ins aprovados</span>
                <span className="stat-value">{revenue.totalApprovedCheckins}</span>
              </div>
            </div>
            <div className="stat-card">
              <span className="stat-icon tint-purple"><IconCreditCard size={20} /></span>
              <div className="stat-body">
                <span className="stat-label">Receita estimada</span>
                <span className="stat-value">{formatMoneyCents(revenue.totalEstimatedRevenueCents)}</span>
              </div>
            </div>
            {revenue.pointsWithoutPriceConfigured > 0 && (
              <div className="stat-card">
                <span className="stat-icon tint-lemon"><IconCreditCard size={20} /></span>
                <div className="stat-body">
                  <span className="stat-label">Pontos sem valor configurado</span>
                  <span className="stat-value">{revenue.pointsWithoutPriceConfigured}</span>
                </div>
              </div>
            )}
          </section>

          {revenue.pointsWithoutPriceConfigured > 0 && (
            <p className="muted">
              Configure o valor por check-in de cada ponto em{" "}
              <Link className="btn-link" to="/pontos-de-checkin" style={{ padding: 0 }}>Pontos de check-in</Link>{" "}
              pra ver a receita estimada completa.
            </p>
          )}

          <section className="panel panel-table">
            <div className="panel-toolbar">
              <h2>Repasse por ponto</h2>
              <button className="btn-ghost" onClick={() => downloadReportCsv("/reports/revenue/export", filter, "relatorio-repasse.csv")}>
                <IconDownload size={16} /> Exportar CSV
              </button>
            </div>
            <table className="data-table">
              <thead><tr><th>Ponto</th><th>App</th><th>Check-ins aprovados</th><th>Valor/check-in</th><th>Receita estimada</th></tr></thead>
              <tbody>
                {revenue.byPoint.length === 0 && <TableEmpty colSpan={5} title="Nenhum ponto de check-in cadastrado" />}
                {revenue.byPoint.map((p) => (
                  <tr key={p.checkinPointId}>
                    <td className="cell-strong">{p.checkinPointName}</td>
                    <td><AppBadge app={p.app} /></td>
                    <td>{p.approvedCheckins}</td>
                    <td className={p.pricePerCheckinCents == null ? "muted" : undefined}>{formatMoneyCents(p.pricePerCheckinCents)}</td>
                    <td className={p.estimatedRevenueCents == null ? "muted" : "cell-strong"}>{formatMoneyCents(p.estimatedRevenueCents)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>

          {appPenetration && (
            <section className="panel">
              <h2>Penetração por app</h2>
              <p className="muted">Quanto da base de {appPenetration.totalActiveStudents} alunos ativos cada app representa.</p>
              <div className="bar-list">
                {appPenetration.items.map((i) => (
                  <div className="bar-row" key={i.app}>
                    <span className="bar-label">{i.app}</span>
                    <div className="bar-track">
                      <div className="bar-fill" style={{ width: `${i.percent * 100}%` }} />
                    </div>
                    <span className="bar-value">{i.activeStudents} ({Math.round(i.percent * 100)}%)</span>
                  </div>
                ))}
              </div>
            </section>
          )}

          {schoolRanking && (
            <section className="panel panel-table">
              <h2>Ranking de unidades</h2>
              <table className="data-table">
                <thead><tr><th>Ponto</th><th>App</th><th>Check-ins no período</th><th>Período anterior</th><th>Variação</th></tr></thead>
                <tbody>
                  {schoolRanking.items.length === 0 && <TableEmpty colSpan={5} title="Sem dados no período" />}
                  {schoolRanking.items.map((i) => (
                    <tr key={i.checkinPointId}>
                      <td className="cell-strong">{i.checkinPointName}</td>
                      <td><AppBadge app={i.app} /></td>
                      <td>{i.totalCheckins}</td>
                      <td>{i.previousPeriodCheckins}</td>
                      <td><ChangeBadge changePercent={i.changePercent} /></td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </section>
          )}
        </>
      )}
    </div>
  );
}

function PeakHoursHeatmap({ cells }: { cells: PeakHourCell[] }) {
  if (cells.length === 0) {
    return <EmptyState title="Sem check-ins no período" hint="Ajuste o filtro de datas para ver resultados." />;
  }

  const totalByKey = new Map(cells.map((c) => [`${c.dayOfWeek}-${c.hour}`, c.totalCheckins]));
  const hours = Array.from(new Set(cells.map((c) => c.hour))).sort((a, b) => a - b);
  const max = Math.max(...cells.map((c) => c.totalCheckins));

  return (
    <div className="heatmap-wrap">
      <div className="heatmap" style={{ gridTemplateColumns: `52px repeat(${WEEKDAYS.length}, 1fr)` }}>
        <div />
        {WEEKDAYS.map((d) => <div key={d.key} className="heatmap-day-label">{d.label}</div>)}
        {hours.map((hour) => (
          <Fragment key={hour}>
            <div className="heatmap-hour-label">{String(hour).padStart(2, "0")}h</div>
            {WEEKDAYS.map((d) => {
              const total = totalByKey.get(`${d.key}-${hour}`) ?? 0;
              const ratio = max === 0 ? 0 : total / max;
              return (
                <div
                  key={d.key}
                  className="heatmap-cell"
                  style={{ background: total === 0 ? "var(--bg)" : `rgba(48, 89, 240, ${0.15 + ratio * 0.75})` }}
                  title={`${d.label} ${hour}h — ${total} check-in${total === 1 ? "" : "s"}`}
                >
                  {total > 0 && total}
                </div>
              );
            })}
          </Fragment>
        ))}
      </div>
    </div>
  );
}

function GrowthChart({ periods, groupBy }: { periods: { periodStart: string; newStudents: number }[]; groupBy: "week" | "month" }) {
  if (periods.length === 0) {
    return <EmptyState title="Sem alunos novos no período" hint="Ajuste o filtro de datas para ver resultados." />;
  }

  const max = Math.max(...periods.map((p) => p.newStudents), 1);

  return (
    <div className="bar-list">
      {periods.map((p) => (
        <div className="bar-row" key={p.periodStart}>
          <span className="bar-label">
            {groupBy === "month"
              ? new Date(p.periodStart).toLocaleDateString("pt-BR", { month: "short", year: "numeric" })
              : `Semana de ${new Date(p.periodStart).toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit" })}`}
          </span>
          <div className="bar-track">
            <div className="bar-fill" style={{ width: `${(p.newStudents / max) * 100}%` }} />
          </div>
          <span className="bar-value">{p.newStudents}</span>
        </div>
      ))}
    </div>
  );
}

/** Variação percentual de um ponto vs. o período anterior — "Novo" (sem período anterior pra
 * comparar) em vez de 0%/infinito quando `changePercent` é nulo. */
function ChangeBadge({ changePercent }: { changePercent?: number | null }) {
  if (changePercent == null) {
    return <span className="badge badge-pending"><span className="dot" />Novo</span>;
  }
  const percent = Math.round(changePercent * 100);
  if (percent > 0) {
    return <span className="badge badge-approved"><span className="dot" />▲ {percent}%</span>;
  }
  if (percent < 0) {
    return <span className="badge badge-rejected"><span className="dot" />▼ {Math.abs(percent)}%</span>;
  }
  return <span className="badge badge-app-wellhub"><span className="dot" />Estável</span>;
}
