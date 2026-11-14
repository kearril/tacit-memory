import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "./client";
import type { components } from "./schema";

export type ServiceLogDays = components["schemas"]["ServiceLogDays"];
export type ServiceLogDay = components["schemas"]["ServiceLogDay"];
export type ServiceLogEntry = components["schemas"]["ServiceLogEntry"];
export type ServiceLogLevel = components["schemas"]["ServiceLogLevel"];

/** The query key every query of the service log starts with, so one invalidation reaches them all. */
export const LOGS_QUERY_KEY = ["logs"] as const;

/** The days with a log file, newest first, and today on the service's machine. */
export function getLogDays(): Promise<ServiceLogDays> {
  return apiFetch<ServiceLogDays>("/api/logs");
}

/** The events of one day, `yyyy-MM-dd`, oldest first; with `after`, only those past that many. */
export function getLogDay(date: string, after = 0): Promise<ServiceLogDay> {
  const query = after === 0 ? "" : `?after=${after}`;
  return apiFetch<ServiceLogDay>(`/api/logs/${encodeURIComponent(date)}${query}`);
}

/** The days with a log file, fetched when the page opens and again when the window regains focus. */
export function useLogDays() {
  return useQuery({ queryKey: [...LOGS_QUERY_KEY, "days"], queryFn: getLogDays, refetchOnWindowFocus: true });
}

/** One day's events, fetched once the day is known. A day without a log is an answer, so a 404 is not retried. */
export function useLogDay(date: string | undefined) {
  return useQuery({
    queryKey: [...LOGS_QUERY_KEY, "day", date],
    queryFn: () => getLogDay(date ?? ""),
    enabled: date !== undefined,
    retry: false,
  });
}
