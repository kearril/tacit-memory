import { api, call } from "./client";
import type { Affects } from "./effects";
import { STATUS_QUERY_KEY } from "./status";
import type { components } from "./schema";

export type ProbeReport = components["schemas"]["ProbeReport"];
export type ProbeResult = components["schemas"]["ProbeResult"];

/**
 * Probes the three auxiliary models as the saved `config.yaml` configures them, as `tacit probe` does; a model that fails
 * is a result, not a failed call. The running service keeps the configuration it started with.
 */
export function runProbe(): Promise<ProbeReport> {
  return call(api.POST("/api/probe"));
}

/** A probe starts or ends a model's cooling down, which the status reports. */
export const PROBE_AFFECTS: Affects = [STATUS_QUERY_KEY];
