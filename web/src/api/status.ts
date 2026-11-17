import { useQuery } from "@tanstack/react-query";
import { api, call } from "./client";
import type { components } from "./schema";

export type StatusReport = components["schemas"]["StatusReport"];
export type BindingProblem = NonNullable<components["schemas"]["BindingProblem"]>;

/** The query key of the status report. */
export const STATUS_QUERY_KEY = ["status"] as const;

/** How often the status report is fetched again while a page that shows it is open. */
const STATUS_POLL_MILLISECONDS = 30_000;

/** The status report: the same columns as `tacit status`, as structured fields. */
export function getStatus(): Promise<StatusReport> {
  return call(api.GET("/api/status"));
}

/**
 * The status report, fetched every 30 seconds and again when the window regains focus. A fetch that fails keeps the last
 * report in `data` and sets `isError`, so a page can go on showing it and say that the connection failed.
 */
export function useStatus() {
  return useQuery({
    queryKey: STATUS_QUERY_KEY,
    queryFn: getStatus,
    refetchInterval: STATUS_POLL_MILLISECONDS,
    refetchOnWindowFocus: true,
  });
}
