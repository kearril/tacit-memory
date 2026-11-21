import { describe, expect, it } from "vitest";
import { complete, EMPTY, presetsLeft } from "./bindings";

describe("binding presets", () => {
  it("offers every preset while nothing is bound", () => {
    expect(presetsLeft([]).map((preset) => preset.client)).toEqual([
      "claude",
      "codex",
      "opencode",
      "pi",
      "omp",
      "agy",
      "dsh",
      "hermes",
    ]);
  });

  it("leaves out a preset whose client is bound, whatever its letter case", () => {
    expect(presetsLeft(["OMP", "other"]).map((preset) => preset.client)).toEqual([
      "claude",
      "codex",
      "opencode",
      "pi",
      "agy",
      "dsh",
      "hermes",
    ]);
  });

  it("fills in all three of the form", () => {
    const [claude] = presetsLeft([]);
    expect(claude).toEqual({ client: "claude", contextFile: "~/.claude/CLAUDE.md", skillsFolder: "~/.claude/skills" });
    expect(complete(claude!)).toBe(true);
  });

  it("needs all three filled in before it can be sent", () => {
    expect(complete(EMPTY)).toBe(false);
    expect(complete({ client: "x", contextFile: "  ", skillsFolder: "~/s" })).toBe(false);
  });
});
