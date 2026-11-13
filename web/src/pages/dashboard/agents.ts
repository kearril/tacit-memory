import antigravityIcon from "@lobehub/icons-static-svg/icons/antigravity-color.svg?url";
import claudeCodeIcon from "@lobehub/icons-static-svg/icons/claudecode-color.svg?url";
import codexIcon from "@lobehub/icons-static-svg/icons/codex-color.svg?url";
import cursorIcon from "@lobehub/icons-static-svg/icons/cursor.svg?url";
import geminiCliIcon from "@lobehub/icons-static-svg/icons/geminicli-color.svg?url";
import openCodeIcon from "@lobehub/icons-static-svg/icons/opencode.svg?url";
import type { components } from "../../api/schema";
import ompIcon from "../../assets/agents/omp.svg?url";

export type ClientStatus = components["schemas"]["ClientStatus"];
export type BindingCheck = components["schemas"]["BindingCheck"];

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
 * The agents the dashboard shows with their own name and logo. The core names no client; only the interface knows these,
 * to look better, and any agent missing here still shows, under its own name with a plain icon.
 */
export const KNOWN_AGENTS: readonly KnownAgent[] = [
  { label: "Claude Code", icon: { url: claudeCodeIcon, monochrome: false }, names: ["claude"] },
  { label: "Antigravity", icon: { url: antigravityIcon, monochrome: false }, names: ["agy", "antigravity"] },
  { label: "omp", icon: { url: ompIcon, monochrome: false }, names: ["omp"] },
  { label: "Codex", icon: { url: codexIcon, monochrome: false }, names: ["codex"] },
  { label: "Gemini CLI", icon: { url: geminiCliIcon, monochrome: false }, names: ["gemini"] },
  { label: "Cursor", icon: { url: cursorIcon, monochrome: true }, names: ["cursor"] },
  { label: "OpenCode", icon: { url: openCodeIcon, monochrome: true }, names: ["opencode"] },
];

/** One agent on the dashboard: its binding, if it has one, and the last day any of its clients connected. */
export interface AgentRow {
  readonly key: string;
  readonly label: string;
  readonly icon: AgentIcon | null;
  readonly binding: BindingCheck | null;
  readonly lastSeen: string | null;
}

/** Whether `name` is `stem` or starts with `stem` and a hyphen, so "claude-code" goes with "claude" but "claudia" does not. */
function goesBy(name: string, stem: string): boolean {
  return name === stem || name.startsWith(`${stem}-`);
}

function knownAgentOf(name: string, known: readonly KnownAgent[]): KnownAgent | null {
  return known.find((agent) => agent.names.some((stem) => goesBy(name, stem))) ?? null;
}

/**
 * The bindings and the clients that connected, merged into one row per agent. Each binding is a row, in the order of the
 * bindings. A client joins the row of the binding that is the same known agent, or whose name the client's name goes by;
 * the clients that join no binding are grouped by known agent (or by their own name) into rows after the bindings, the
 * most recently seen first. A row's last-seen day is the latest of its clients.
 */
export function agentsOf(
  bindings: readonly BindingCheck[],
  clients: readonly ClientStatus[],
  known: readonly KnownAgent[] = KNOWN_AGENTS,
): AgentRow[] {
  interface Draft {
    key: string;
    label: string;
    agent: KnownAgent | null;
    binding: BindingCheck | null;
    lastSeen: string | null;
  }

  const bound: Draft[] = bindings.map((binding) => {
    const agent = knownAgentOf(binding.client, known);
    return { key: `binding:${binding.client}`, label: agent?.label ?? binding.client, agent, binding, lastSeen: null };
  });
  const unbound: Draft[] = [];

  for (const client of clients) {
    const agent = knownAgentOf(client.name, known);
    const row =
      bound.find((draft) => (agent !== null && draft.agent === agent) || goesBy(client.name, draft.binding!.client)) ??
      unbound.find((draft) => (agent !== null ? draft.agent === agent : draft.key === `client:${client.name}`));
    if (row === undefined) {
      unbound.push({
        key: agent === null ? `client:${client.name}` : `agent:${agent.label}`,
        label: agent?.label ?? client.name,
        agent,
        binding: null,
        lastSeen: client.lastSeen,
      });
    } else if (row.lastSeen === null || client.lastSeen > row.lastSeen) {
      row.lastSeen = client.lastSeen;
    }
  }

  unbound.sort((a, b) => (b.lastSeen ?? "").localeCompare(a.lastSeen ?? ""));
  return [...bound, ...unbound].map((draft) => ({
    key: draft.key,
    label: draft.label,
    icon: draft.agent?.icon ?? null,
    binding: draft.binding,
    lastSeen: draft.lastSeen,
  }));
}
