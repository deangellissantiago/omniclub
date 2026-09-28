import { apiClient } from "./client";
import type { BookingListItem, DateRangeFilter } from "./types";

export async function listBookings(filter: DateRangeFilter): Promise<BookingListItem[]> {
  const { data } = await apiClient.get<BookingListItem[]>("/bookings", { params: filter });
  return data;
}
