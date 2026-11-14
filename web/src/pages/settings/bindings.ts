import type { ConnectRequest } from "../../api/bindings";

/**
 * The binding presets: the client name, context file and skills folder of clients whose paths are known, to fill in the
 * form. The service does not know whether a binding came from a preset.
 */
export const PRESETS: readonly ConnectRequest[] = [
  { client: "claude", contextFile: "~/.claude/CLAUDE.md", skillsFolder: "~/.claude/skills" },
  { client: "omp", contextFile: "~/.omp/agent/AGENTS.md", skillsFolder: "~/.omp/agent/skills" },
  { client: "agy", contextFile: "~/.gemini/config/AGENTS.md", skillsFolder: "~/.gemini/config/skills" },
];

/** An empty form. */
export const EMPTY: ConnectRequest = { client: "", contextFile: "", skillsFolder: "" };

/** The presets whose client is not bound yet, in their order; a bound name is compared without letter case. */
export function presetsLeft(bound: readonly string[]): readonly ConnectRequest[] {
  const names = new Set(bound.map((name) => name.toLowerCase()));
  return PRESETS.filter((preset) => !names.has(preset.client));
}

/** Whether the form can be sent: each of the three is filled in. */
export function complete(request: ConnectRequest): boolean {
  return request.client.trim() !== "" && request.contextFile.trim() !== "" && request.skillsFolder.trim() !== "";
}
