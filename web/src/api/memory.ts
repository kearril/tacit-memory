import { apiFetch } from "./client";
import type { components } from "./schema";

export type RecallTraceRequest = components["schemas"]["RecallTraceRequest"];
export type RecallTraceReport = components["schemas"]["RecallTraceReport"];
export type TraceUnit = components["schemas"]["TraceUnit"];
export type TraceResult = components["schemas"]["TraceResult"];
export type UnitFate = components["schemas"]["UnitFate"];
export type FullTextMethod = components["schemas"]["FullTextMethod"];

/**
 * Runs a recall trace: the search `recall` would run for the same parameters, stage by stage. It counts no return and
 * changes no file. Bad parameters are refused with every problem (`RequestInvalid`).
 */
export function traceRecall(request: RecallTraceRequest): Promise<RecallTraceReport> {
  return apiFetch<RecallTraceReport>("/api/recall/trace", { method: "POST", body: JSON.stringify(request) });
}
