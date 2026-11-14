import { describe, expect, it } from "vitest";
import type { StatusReport } from "../../api/status";
import { targetOf, todosOf } from "./todos";

const columns = { title: "", lines: [] };

/** A report in which nothing needs the user: all models available, one sound binding, no extra listener, a good backup. */
function healthy(): StatusReport {
  return {
    models: {
      ...columns,
      items: [
        { role: "embedding", state: "available", coolingSeconds: null },
        { role: "reranker", state: "available", coolingSeconds: null },
        { role: "decision", state: "available", coolingSeconds: null },
      ],
    },
    bindings: {
      ...columns,
      items: [
        { client: "claude", contextFile: "C:/c.md", skillsFolder: "C:/s", problem: null, skillLink: null, sharedWith: [] },
      ],
    },
    service: {
      ...columns,
      running: true,
      endpoint: null,
      version: "1.0.0",
      uptimeSeconds: 60,
      otherProgramAnswered: false,
      webAddress: "http://127.0.0.1:18888/ui",
      extraListener: null,
    },
    backups: {
      ...columns,
      latest: null,
      latestAt: null,
      lastRemote: { at: "2026-11-12T03:00:00Z", succeeded: true, error: null },
    },
    pending: { ...columns, candidates: 4, formatProblems: 2, workstreamEmpty: false },
  };
}

describe("todosOf", () => {
  it("lists nothing when nothing needs the user, even with pending candidates and format problems", () => {
    expect(todosOf(healthy())).toEqual([]);
  });

  it("lists the unconfigured models as one to-do that names them all, in the report's order", () => {
    const report = healthy();
    report.models!.items[0] = { role: "embedding", state: "unconfigured", coolingSeconds: null };
    report.models!.items[2] = { role: "decision", state: "unconfigured", coolingSeconds: null };

    expect(todosOf(report)).toEqual([{ kind: "modelsUnconfigured", roles: ["embedding", "decision"] }]);
  });

  it("lists a single unconfigured model as well", () => {
    const report = healthy();
    report.models!.items[1] = { role: "reranker", state: "unconfigured", coolingSeconds: null };

    expect(todosOf(report)).toEqual([{ kind: "modelsUnconfigured", roles: ["reranker"] }]);
  });

  it("does not list a cooling model", () => {
    const report = healthy();
    report.models!.items[1] = { role: "reranker", state: "coolingDown", coolingSeconds: 120 };

    expect(todosOf(report)).toEqual([]);
  });

  it("lists a missing binding", () => {
    const report = healthy();
    report.bindings!.items = [];

    expect(todosOf(report)).toEqual([{ kind: "noBindings" }]);
  });

  it("lists each binding with a problem on its own", () => {
    const report = healthy();
    const sound = report.bindings!.items[0];
    report.bindings!.items = [
      { ...sound, client: "claude", problem: "contextFileMissing" },
      sound,
      { ...sound, client: "codex", problem: "skillLinkNotTacit" },
    ];

    expect(todosOf(report)).toEqual([
      { kind: "bindingProblem", client: "claude", problem: "contextFileMissing" },
      { kind: "bindingProblem", client: "codex", problem: "skillLinkNotTacit" },
    ]);
  });

  it("lists an extra listener that failed, with its reason, but not one that is listening", () => {
    const report = healthy();
    report.service!.extraListener = { address: "100.64.0.10", failureReason: null };
    expect(todosOf(report)).toEqual([]);

    report.service!.extraListener = { address: "100.64.0.10", failureReason: "The address is in use." };
    expect(todosOf(report)).toEqual([
      { kind: "extraListenerFailed", address: "100.64.0.10", reason: "The address is in use." },
    ]);
  });

  it("lists a failed last cloud backup, but not a missing record", () => {
    const report = healthy();
    report.backups!.lastRemote = { at: "2026-11-12T03:00:00Z", succeeded: false, error: "401 Unauthorized" };
    expect(todosOf(report)).toEqual([
      { kind: "cloudBackupFailed", at: "2026-11-12T03:00:00Z", error: "401 Unauthorized" },
    ]);

    report.backups!.lastRemote = null;
    expect(todosOf(report)).toEqual([]);
  });

  it("adds nothing for a column the report leaves out", () => {
    expect(todosOf({})).toEqual([]);
  });

  it("keeps the order: models, bindings, extra listener, cloud backup", () => {
    const report = healthy();
    report.backups!.lastRemote = { at: "2026-11-12T03:00:00Z", succeeded: false, error: null };
    report.service!.extraListener = { address: "100.64.0.10", failureReason: "in use" };
    report.bindings!.items = [];
    report.models!.items[2] = { role: "decision", state: "unconfigured", coolingSeconds: null };

    expect(todosOf(report).map((todo) => todo.kind)).toEqual([
      "modelsUnconfigured",
      "noBindings",
      "extraListenerFailed",
      "cloudBackupFailed",
    ]);
  });
});

describe("targetOf", () => {
  it("leads each kind to where it is fixed", () => {
    expect(targetOf({ kind: "modelsUnconfigured", roles: ["decision"] })).toBe("/settings#settings-models");
    expect(targetOf({ kind: "noBindings" })).toBe("/settings#settings-bindings");
    expect(targetOf({ kind: "bindingProblem", client: "claude", problem: "markersIncomplete" })).toBe("/settings#settings-bindings");
    expect(targetOf({ kind: "extraListenerFailed", address: "x", reason: "y" })).toBe("/settings#settings-access");
    expect(targetOf({ kind: "cloudBackupFailed", at: "x", error: null })).toBe("/dashboard#backups");
  });
});
