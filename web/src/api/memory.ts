import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "./client";
import type { components } from "./schema";

export type RecallTraceRequest = components["schemas"]["RecallTraceRequest"];
export type RecallTraceReport = components["schemas"]["RecallTraceReport"];
export type TraceUnit = components["schemas"]["TraceUnit"];
export type TraceResult = components["schemas"]["TraceResult"];
export type UnitFate = components["schemas"]["UnitFate"];
export type FullTextMethod = components["schemas"]["FullTextMethod"];

export const RECALL_TRACE_QUERY_KEY = ["recall-trace"] as const;

/**
 * Runs a recall trace: the search `recall` would run for the same parameters, stage by stage. It counts no return and
 * changes no file. Bad parameters are refused with every problem (`RequestInvalid`).
 */
export function traceRecall(request: RecallTraceRequest): Promise<RecallTraceReport> {
  return apiFetch<RecallTraceReport>("/api/recall/trace", { method: "POST", body: JSON.stringify(request) });
}

/**
 * The trace of `request`, run once per distinct request; `null` runs nothing. A trace is a snapshot the user asks for,
 * so it is never refetched on its own: running the same search again is an explicit refetch.
 */
export function useRecallTrace(request: RecallTraceRequest | null) {
  return useQuery({
    queryKey: [...RECALL_TRACE_QUERY_KEY, request],
    queryFn: () => traceRecall(request!),
    enabled: request !== null,
    retry: false,
    refetchOnWindowFocus: false,
    staleTime: Infinity,
  });
}
