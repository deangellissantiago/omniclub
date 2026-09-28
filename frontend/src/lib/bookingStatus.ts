import type { BookingStatus } from "../api/types";

export const BOOKING_STATUS_LABEL: Record<BookingStatus, string> = {
  Requested: "Solicitada",
  Confirmed: "Confirmada",
  Rejected: "Rejeitada (sem vaga)",
  Canceled: "Cancelada",
  LateCanceled: "Cancelada em cima da hora",
};
