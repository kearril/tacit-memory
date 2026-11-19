import { api, call } from "./client";
import type { Affects } from "./effects";
import { MEMORY_QUERY_KEY } from "./memory";
import { STATUS_QUERY_KEY } from "./status";
import type { components } from "./schema";

export type SyncResult = components["schemas"]["SyncResult"];
export type SyncRecord = components["schemas"]["SyncRecord"];
export type SyncOutcome = components["schemas"]["SyncOutcome"];

/**
 * Syncs the memory repository with its remote once, as `tacit sync` does without choosing a side; a maintenance
 * operation. A sync that fails, is held back or pauses is a result with its report, not a failed call.
 */
export function runSync(): Promise<SyncResult> {
  return call(api.POST("/api/sync"));
}

/** A sync may pull what another machine wrote, which changes any memory view, and moves the sync the status reports. */
export const SYNC_AFFECTS: Affects = [MEMORY_QUERY_KEY, STATUS_QUERY_KEY];

/** Whether each outcome waits for a side to be chosen with one of `SIDE_COMMANDS`. */
export const CHOOSES_SIDE: Readonly<Record<SyncOutcome, boolean>> = {
  synced: false,
  failed: false,
  handEdits: false,
  conflict: false,
  unrelated: true,
  restorePaused: true,
};

/** The commands that choose a side, which only the command line runs. */
export const SIDE_COMMANDS = { local: "tacit sync --use-local", remote: "tacit sync --use-remote" } as const;
