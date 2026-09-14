import { useEffect, useState } from "react";
import { getGeneralReport, getReportByStudent, getReportBySchool } from "../api/reports";
import type { GeneralReport, SchoolReportItem, StudentReportItem } from "../api/types";
import { extractErrorMessage } from "../api/client";
import { DateRangeFilter } from "../components/DateRangeFilter";
import { AppBadge, Avatar, IconCheckCircle, IconMapPin, TableEmpty } from "../components/ui";

type Tab = "student" | "school" | "general";

function formatDate(value?: string | null): string {
  return value ? new Date(value).toLocaleString("pt-BR") : "-";
}

export function ReportsPage() {
  const [tab, setTab] = useState<Tab>("general");
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [error, setError] = useState<string | null>(null);

  const [general, setGeneral] = useState<GeneralReport | null>(null);
  const [byStudent, setByStudent] = useState<StudentReportItem[]>([]);
  const [bySchool, setBySchool] = useState<SchoolReportItem[]>([]);

  const filter = { startDate: startDate || undefined, endDate: endDate || undefined };

  useEffect(() => {
    setError(null);
    const load = tab === "general"
      ? getGeneralReport(filter).then(setGeneral)
      : tab === "student"
        ? getReportByStudent(filter).then(setByStudent)
        : getReportBySchool(filter).then(setBySchool);

    load.catch((err) => setError(extractErrorMessage(err)));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [tab, startDate, endDate]);

  return (
    <div>
      <div className="page-header">
        <h1>Relatórios</h1>
        <p className="muted">Acompanhe o desempenho do clube por aluno, por unidade ou na visão geral.</p>
      </div>

      <div className="tabs">
        <button className={`tab ${tab === "general" ? "active" : ""}`} onClick={() => setTab("general")}>Geral</button>
        <button className={`tab ${tab === "student" ? "active" : ""}`} onClick={() => setTab("student")}>Por aluno</button>
        <button className={`tab ${tab === "school" ? "active" : ""}`} onClick={() => setTab("school")}>Por escola</button>
      </div>

      <DateRangeFilter startDate={startDate} endDate={endDate} onChange={(s, e) => { setStartDate(s); setEndDate(e); }} />

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
          <h2>Check-ins por aluno</h2>
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
          <h2>Check-ins por escola/unidade</h2>
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
    </div>
  );
}
