import { apiFetch } from "./client";
import type { components } from "./schema";

export type SessionStatus = components["schemas"]["SessionStatus"];
export type AccessPasswordBody = components["schemas"]["AccessPasswordBody"];

/** The query key of the session status. Every answer that changes the session writes its status under this key. */
export const SESSION_QUERY_KEY = ["session"] as const;

/** The session status of the current connection: whether it is local, its login state, the memory language and the version. */
export function getSession(): Promise<SessionStatus> {
  return apiFetch<SessionStatus>("/api/session");
}

/** Logs in with the access password. On success the answer carries the new session status and the session cookie. */
export function login(password: string): Promise<SessionStatus> {
  return apiFetch<SessionStatus>("/api/session", { method: "POST", body: JSON.stringify({ password }) });
}

/** Logs out this browser: its session cookie expires. */
export function logout(): Promise<SessionStatus> {
  return apiFetch<SessionStatus>("/api/session", { method: "DELETE" });
}

/** Sets or changes the access password. A remote connection must give the current password. */
export function setAccessPassword(body: AccessPasswordBody): Promise<SessionStatus> {
  return apiFetch<SessionStatus>("/api/access-password", { method: "PUT", body: JSON.stringify(body) });
}

/** Clears the access password. Only a local connection may do this. */
export function clearAccessPassword(): Promise<SessionStatus> {
  return apiFetch<SessionStatus>("/api/access-password", { method: "DELETE" });
}
