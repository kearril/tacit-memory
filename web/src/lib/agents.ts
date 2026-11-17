import antigravityIcon from "@lobehub/icons-static-svg/icons/antigravity-color.svg?url";
import claudeCodeIcon from "@lobehub/icons-static-svg/icons/claudecode-color.svg?url";
import codexIcon from "@lobehub/icons-static-svg/icons/codex-color.svg?url";
import cursorIcon from "@lobehub/icons-static-svg/icons/cursor.svg?url";
import geminiCliIcon from "@lobehub/icons-static-svg/icons/geminicli-color.svg?url";
import openCodeIcon from "@lobehub/icons-static-svg/icons/opencode.svg?url";
import ompIcon from "../assets/agents/omp.svg?url";

/** The logo of an agent: a file of its own colours, or a one-colour shape that takes the colour of the text around it. */
export interface AgentIcon {
  readonly url: string;
  readonly monochrome: boolean;
}

/** An agent the interface knows by sight: its name, its logo, and the names a binding or a client of it goes by. */
export interface KnownAgent {
  readonly label: string;
  readonly icon: AgentIcon;
  /** A binding or a client belongs to the agent when its name is one of these, or one of these followed by a hyphen. */
  readonly names: readonly string[];
}

/**
 * The agents the interface shows with their own name and logo. The core names no client; only the interface knows these,
 * to look better, and any agent missing here still shows, under its own name with a plain icon.
 */
export const KNOWN_AGENTS: readonly KnownAgent[] = [
  { label: "Claude Code", icon: { url: claudeCodeIcon, monochrome: false }, names: ["claude"] },
  { label: "Antigravity", icon: { url: antigravityIcon, monochrome: false }, names: ["agy", "antigravity"] },
  { label: "Oh My Pi", icon: { url: ompIcon, monochrome: false }, names: ["omp"] },
  { label: "Codex", icon: { url: codexIcon, monochrome: false }, names: ["codex"] },
  { label: "Gemini CLI", icon: { url: geminiCliIcon, monochrome: false }, names: ["gemini"] },
  { label: "Cursor", icon: { url: cursorIcon, monochrome: true }, names: ["cursor"] },
  { label: "OpenCode", icon: { url: openCodeIcon, monochrome: true }, names: ["opencode"] },
];

/** Whether `name` is `stem` or starts with `stem` and a hyphen, so "claude-code" goes with "claude" but "claudia" does not. */
export function goesBy(name: string, stem: string): boolean {
  return name === stem || name.startsWith(`${stem}-`);
}

/** The known agent a binding or client name goes by, if any. */
export function knownAgentOf(name: string, known: readonly KnownAgent[] = KNOWN_AGENTS): KnownAgent | null {
  return known.find((agent) => agent.names.some((stem) => goesBy(name, stem))) ?? null;
}
