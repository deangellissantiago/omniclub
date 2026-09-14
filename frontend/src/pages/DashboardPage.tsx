import { useEffect, useState } from "react";
import { getDashboardSummary } from "../api/dashboard";
import type { DashboardSummary } from "../api/types";
import { extractErrorMessage } from "../api/client";
import {
  AppBadge,
  Avatar,
  IconCheckCircle,
  IconMapPin,
  IconUsers,
  StatusBadge,
  TableEmpty,
} from "../components/ui";

function formatDateTime(value: string): string {
  return new Date(value).toLocaleString("pt-BR");
}

const APP_TINT: Record<string, string> = { Wellhub: "tint-sky", TotalPass: "tint-pink" };

export function DashboardPage() {
  const [summary, setSummary] = useState<DashboardSummary | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getDashboardSummary()
      .then(setSummary)
      .catch((err) => setError(extractErrorMessage(err)));
  }, []);

  if (error) return <p className="error-text">{error}</p>;
  if (!summary) return <p className="muted">Carregando...</p>;

  return (
    <div>
      <div className="hero-card">
        <div className="hero-copy">
          <p className="hero-title">
            Seus benefícios no piloto <em>automático</em>
          </p>
          <p className="hero-sub">
            Cada check-in do Wellhub e do TotalPass é recebido, aprovado e vira dado na hora — sem intervenção manual.
          </p>
        </div>
        <svg className="hero-art" viewBox="0 0 430 150" fill="none" aria-hidden="true" preserveAspectRatio="xMidYMid meet">
          <path
            d="M-10 118 C70 118 85 44 175 44 S300 104 440 62"
            stroke="rgba(255,255,255,0.35)"
            strokeWidth="2"
            strokeDasharray="1 8"
            strokeLinecap="round"
          />
          {[
            { x: 62, y: 100, accent: false },
            { x: 175, y: 44, accent: false },
            { x: 340, y: 82, accent: true },
          ].map((n) => (
            <g key={`${n.x}-${n.y}`}>
              <circle cx={n.x} cy={n.y} r="16" fill="rgba(255,255,255,0.13)" stroke={n.accent ? "#FFFE78" : "rgba(255,255,255,0.45)"} strokeWidth="1.6" />
              <path
                d={`M${n.x - 5.5} ${n.y + 0.5}l4 4 7-8`}
                stroke={n.accent ? "#FFFE78" : "#fff"}
                strokeWidth="2.4"
                strokeLinecap="round"
                strokeLinejoin="round"
              />
            </g>
          ))}
        </svg>
      </div>

      <section className="cards-row">
        <div className="stat-card">
          <span className="stat-icon tint-blue"><IconCheckCircle size={20} /></span>
          <div className="stat-body">
            <span className="stat-label">Total de check-ins</span>
            <span className="stat-value">{summary.totalCheckins}</span>
          </div>
        </div>
        <div className="stat-card">
          <span className="stat-icon tint-purple"><IconUsers size={20} /></span>
          <div className="stat-body">
            <span className="stat-label">Alunos cadastrados</span>
            <span className="stat-value">{summary.totalStudents}</span>
          </div>
        </div>
        {summary.checkinsByApp.map((item) => (
          <div className="stat-card" key={`checkins-${item.app}`}>
            <span className={`stat-icon ${APP_TINT[item.app] ?? "tint-lemon"}`}><IconMapPin size={20} /></span>
            <div className="stat-body">
              <span className="stat-label">Check-ins via {item.app}</span>
              <span className="stat-value">{item.total}</span>
            </div>
          </div>
        ))}
        {summary.studentsByApp.map((item) => (
          <div className="stat-card" key={`students-${item.app}`}>
            <span className={`stat-icon ${APP_TINT[item.app] ?? "tint-lemon"}`}><IconUsers size={20} /></span>
            <div className="stat-body">
              <span className="stat-label">Alunos com {item.app}</span>
              <span className="stat-value">{item.total}</span>
            </div>
          </div>
        ))}
      </section>

      <section className="panel panel-table">
        <h2>Últimos check-ins</h2>
        <table className="data-table">
          <thead>
            <tr>
              <th>Aluno</th>
              <th>Ponto de check-in</th>
              <th>App</th>
              <th>Data/hora</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {summary.recentCheckins.length === 0 && (
              <TableEmpty
                colSpan={5}
                title="Nenhum check-in registrado ainda"
                hint="Assim que um aluno fizer check-in pelo app, ele aparecerá aqui."
              />
            )}
            {summary.recentCheckins.map((c) => (
              <tr key={c.id}>
                <td>
                  <span className="cell-person">
                    <Avatar name={c.studentName} small />
                    <span className="cell-strong">{c.studentName ?? "Não identificado"}</span>
                  </span>
                </td>
                <td>{c.checkinPointName ?? "-"}</td>
                <td><AppBadge app={c.app} /></td>
                <td>{formatDateTime(c.occurredAt)}</td>
                <td><StatusBadge status={c.status} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
