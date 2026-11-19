import { QueryClient, type QueryKey } from "@tanstack/react-query";
import { describe, expect, it } from "vitest";
import { ACTIVITY_QUERY_KEY } from "./activity";
import { BACKUPS_QUERY_KEY, BACKUP_AFFECTS, REINDEX_AFFECTS, RESTORE_AFFECTS } from "./backups";
import { BIND_AFFECTS, BINDINGS_QUERY_KEY } from "./bindings";
import { CONFIG_QUERY_KEY, SAVE_CONFIG_AFFECTS } from "./config";
import { afterCommand, type Affects } from "./effects";
import { LOGS_QUERY_KEY } from "./logs";
import {
  ACTIVITY_MONTHS_QUERY_KEY,
  MEMORY_QUERY_KEY,
  PAGES_QUERY_KEY,
  PERSONA_QUERY_KEY,
  RECALL_TRACE_QUERY_KEY,
  WORKSTREAMS_QUERY_KEY,
} from "./memory";
import { PROBE_AFFECTS } from "./probe";
import { PROMPTS_QUERY_KEY, SAVE_PROMPT_AFFECTS } from "./prompts";
import { SESSION_QUERY_KEY } from "./session";
import { STATUS_QUERY_KEY } from "./status";
import { SYNC_AFFECTS } from "./sync";

/** One cached query of each kind the interface reads, with the parameters some of them take. */
const CACHED: Readonly<Record<string, QueryKey>> = {
  status: STATUS_QUERY_KEY,
  backups: BACKUPS_QUERY_KEY,
  bindings: BINDINGS_QUERY_KEY,
  config: CONFIG_QUERY_KEY,
  prompts: PROMPTS_QUERY_KEY,
  logDays: [...LOGS_QUERY_KEY, "days"],
  session: SESSION_QUERY_KEY,
  heatmap: ACTIVITY_QUERY_KEY,
  persona: PERSONA_QUERY_KEY,
  page: [...PAGES_QUERY_KEY, "web-ui"],
  workstream: [...WORKSTREAMS_QUERY_KEY, "tacit-memory"],
  month: [...ACTIVITY_MONTHS_QUERY_KEY, "2026-11"],
  trace: [...RECALL_TRACE_QUERY_KEY, { query: "备份" }],
};

/** The names of the cached queries a command with `affects` leaves invalidated. */
async function invalidatedBy(affects: Affects): Promise<string[]> {
  const client = new QueryClient();
  for (const key of Object.values(CACHED)) {
    client.setQueryData(key, {});
  }
  await afterCommand(client, affects);
  return Object.entries(CACHED)
    .filter(([, key]) => client.getQueryState(key)?.isInvalidated === true)
    .map(([name]) => name)
    .sort();
}

describe("afterCommand", () => {
  it("invalidates every query after a restore, the memory views and the trace among them", async () => {
    expect(await invalidatedBy(RESTORE_AFFECTS)).toEqual(Object.keys(CACHED).sort());
  });

  it("invalidates the memory and the status after a reindex, and not the config or the prompts", async () => {
    expect(await invalidatedBy(REINDEX_AFFECTS)).toEqual(
      ["heatmap", "month", "page", "persona", "status", "trace", "workstream"].sort(),
    );
  });

  it("invalidates the memory and the status after a sync, which may pull what another machine wrote", async () => {
    expect(await invalidatedBy(SYNC_AFFECTS)).toEqual(
      ["heatmap", "month", "page", "persona", "status", "trace", "workstream"].sort(),
    );
  });

  it("invalidates only the status after a probe", async () => {
    expect(await invalidatedBy(PROBE_AFFECTS)).toEqual(["status"]);
  });

  it("invalidates only the config and the status after a settings save", async () => {
    expect(await invalidatedBy(SAVE_CONFIG_AFFECTS)).toEqual(["config", "status"]);
  });

  it("invalidates the backups and the status after a backup", async () => {
    expect(await invalidatedBy(BACKUP_AFFECTS)).toEqual(["backups", "status"]);
  });

  it("invalidates the bindings and the status after a bind or an unbind", async () => {
    expect(await invalidatedBy(BIND_AFFECTS)).toEqual(["bindings", "status"]);
  });

  it("invalidates the prompts and the status after a prompt save", async () => {
    expect(await invalidatedBy(SAVE_PROMPT_AFFECTS)).toEqual(["prompts", "status"]);
  });
});

describe("memory query keys", () => {
  it("keeps the dashboard heatmap and the recall trace under the memory prefix", () => {
    for (const key of [ACTIVITY_QUERY_KEY, RECALL_TRACE_QUERY_KEY]) {
      expect(key.slice(0, MEMORY_QUERY_KEY.length)).toEqual(MEMORY_QUERY_KEY);
    }
  });
});
