import { useEffect, useState } from "react";
import { listCheckins } from "../api/checkins";
import type { CheckinRecord } from "../api/types";
import { extractErrorMessage } from "../api/client";
import { DateRangeFilter } from "../components/DateRangeFilter";
import { AppBadge, Avatar, StatusBadge, TableEmpty } from "../components/ui";

function formatDateTime(value: string): string {
  return new Date(value).toLocaleString("pt-BR");
}

export function CheckinsPage() {
  const [checkins, setCheckins] = useState<CheckinRecord[]>([]);
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    listCheckins({ startDate: startDate || undefined, endDate: endDate || undefined })
      .then(setCheckins)
      .catch((err) => setError(extractErrorMessage(err)));
  }, [startDate, endDate]);

  return (
    <div>
      <div className="page-header">
        <h1>Check-ins</h1>
        <p className="muted">Todo check-in recebido dos aplicativos é aprovado automaticamente.</p>
      </div>

      <DateRangeFilter startDate={startDate} endDate={endDate} onChange={(s, e) => { setStartDate(s); setEndDate(e); }} />

      {error && <p className="error-text">{error}</p>}

      <section className="panel panel-table">
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
            {checkins.length === 0 && (
              <TableEmpty
                colSpan={5}
                title="Nenhum check-in encontrado para o período"
                hint="Ajuste o filtro de datas ou aguarde novos check-ins."
              />
            )}
            {checkins.map((c) => (
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
