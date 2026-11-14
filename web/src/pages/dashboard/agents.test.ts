import { describe, expect, it } from "vitest";
import { agentsOf, type BindingCheck, type ClientStatus, type KnownAgent } from "./agents";

const known: KnownAgent[] = [
  { label: "Claude Code", icon: { url: "claude.svg", monochrome: false }, names: ["claude"] },
  { label: "Antigravity", icon: { url: "agy.svg", monochrome: false }, names: ["agy", "antigravity"] },
];

function binding(client: string, problem: BindingCheck["problem"] = null): BindingCheck {
  return { client, contextFile: "C:/c.md", skillsFolder: "C:/s", problem, skillLink: null, sharedWith: [] };
}

function client(name: string, lastSeen: string): ClientStatus {
  return { name, lastSeen, protocolVersion: "2026-07-28", capabilities: [], extensions: [] };
}

describe("agentsOf", () => {
  it("joins a client to the binding of the same known agent, even under another name", () => {
    const rows = agentsOf([binding("agy"), binding("claude")], [client("antigravity-client", "2026-11-13")], known);

    expect(rows.map((row) => [row.label, row.binding?.client ?? null, row.lastSeen])).toEqual([
      ["Antigravity", "agy", "2026-11-13"],
      ["Claude Code", "claude", null],
    ]);
    expect(rows[0].icon).toEqual({ url: "agy.svg", monochrome: false });
  });

  it("joins an unknown client to the binding its name goes by, but not to a binding that only shares letters", () => {
    const rows = agentsOf(
      [binding("omp"), binding("zed")],
      [client("omp-coding-agent", "2026-11-12"), client("zedd", "2026-11-11")],
      known,
    );

    expect(rows.map((row) => [row.label, row.binding?.client ?? null, row.lastSeen, row.icon])).toEqual([
      ["omp", "omp", "2026-11-12", null],
      ["zed", "zed", null, null],
      ["zedd", null, "2026-11-11", null],
    ]);
  });

  it("takes the latest day of the clients that join one row", () => {
    const rows = agentsOf(
      [binding("claude")],
      [client("claude-code", "2026-11-10"), client("claude-desktop", "2026-11-12")],
      known,
    );

    expect(rows).toHaveLength(1);
    expect(rows[0].lastSeen).toBe("2026-11-12");
  });

  it("lists the clients without a binding after the bindings, grouped by known agent, the most recently seen first", () => {
    const rows = agentsOf(
      [binding("claude", "markersIncomplete")],
      [client("other", "2026-11-01"), client("antigravity", "2026-11-05"), client("antigravity-client", "2026-11-09")],
      known,
    );

    expect(rows.map((row) => [row.key, row.lastSeen])).toEqual([
      ["binding:claude", null],
      ["agent:Antigravity", "2026-11-09"],
      ["client:other", "2026-11-01"],
    ]);
    expect(rows[0].binding?.problem).toBe("markersIncomplete");
  });

  it("lists nothing when there are neither bindings nor clients", () => {
    expect(agentsOf([], [], known)).toEqual([]);
  });
});
