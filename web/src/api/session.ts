import { apiFetch } from "./client";
import type { components } from "./schema";

export type SessionStatus = components["schemas"]["SessionStatus"];

/** The session status of the current connection: whether it is local, its login state, the memory language and the version. */
export function getSession(): Promise<SessionStatus> {
  return apiFetch<SessionStatus>("/api/session");
}
