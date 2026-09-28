import { useEffect, useState } from "react";
import { listBookings } from "../api/bookings";
import type { BookingListItem } from "../api/types";
import { extractErrorMessage } from "../api/client";
import { DateRangeFilter } from "../components/DateRangeFilter";
import { Avatar, BookingStatusBadge, TableEmpty } from "../components/ui";

function formatDateTime(value?: string | null): string {
  return value ? new Date(value).toLocaleString("pt-BR") : "-";
}

export function BookingsPage() {
  const [bookings, setBookings] = useState<BookingListItem[]>([]);
  const [startDate, setStartDate] = useState("");
  const [endDate, setEndDate] = useState("");
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    listBookings({ startDate: startDate || undefined, endDate: endDate || undefined })
      .then(setBookings)
      .catch((err) => setError(extractErrorMessage(err)));
  }, [startDate, endDate]);

  return (
    <div>
      <div className="page-header">
        <h1>Reservas</h1>
        <p className="muted">
          Reservas feitas pelo app Wellhub nas aulas da sua agenda — confirmadas automaticamente enquanto houver vaga.
        </p>
      </div>

      <DateRangeFilter startDate={startDate} endDate={endDate} onChange={(s, e) => { setStartDate(s); setEndDate(e); }} />

      {error && <p className="error-text">{error}</p>}

      <section className="panel panel-table">
        <table className="data-table">
          <thead>
            <tr>
              <th>Aluno</th>
              <th>Aula</th>
              <th>Unidade</th>
              <th>Horário da aula</th>
              <th>Reservado em</th>
              <th>Status</th>
            </tr>
          </thead>
          <tbody>
            {bookings.length === 0 && (
              <TableEmpty
                colSpan={6}
                title="Nenhuma reserva encontrada para o período"
                hint="Cadastre horários em Agenda de aulas para os alunos poderem reservar pelo Wellhub."
              />
            )}
            {bookings.map((b) => (
              <tr key={b.id}>
                <td>
                  <span className="cell-person">
                    <Avatar name={b.studentName} small />
                    <span className="cell-strong">{b.studentName ?? `Wellhub ${b.gympassId}`}</span>
                  </span>
                </td>
                <td>{b.className ?? "-"}</td>
                <td>{b.checkinPointName ?? "-"}</td>
                <td>{formatDateTime(b.slotStartsAt)}</td>
                <td>{formatDateTime(b.requestedAt)}</td>
                <td><BookingStatusBadge status={b.status} /></td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
