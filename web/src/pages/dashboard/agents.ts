import type { components } from "../../api/schema";
import { type AgentIcon, goesBy, KNOWN_AGENTS, type KnownAgent, knownAgentOf } from "../../lib/agents";

export type ClientStatus = components["schemas"]["ClientStatus"];
export type BindingCheck = components["schemas"]["BindingCheck"];

/** One agent on the dashboard: its binding, if it has one, and the last day any of its clients connected. */
export interface AgentRow {
  readonly key: string;
  readonly label: string;
  readonly icon: AgentIcon | null;
  readonly binding: BindingCheck | null;
  readonly lastSeen: string | null;
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
