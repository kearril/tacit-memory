/** The base the interface is served under. Routes inside the interface are written without it. */
export const UI_BASE = "/ui";

/** A router location, as react-router reports it: the path below the base, with its query and hash. */
export interface RouterLocation {
  readonly pathname: string;
  readonly search: string;
  readonly hash: string;
}

/**
 * Accepts a return target only when it is a path inside the interface: it starts with `/ui/`, does not continue with a
 * second slash (which a browser reads as another host), has no backslash (which a browser reads as a slash), and has no
 * control characters. Anything else, including a value that is not a string, is rejected.
 */
export function safeReturnTarget(value: unknown): string | null {
  if (typeof value !== "string" || !value.startsWith(`${UI_BASE}/`) || value.startsWith(`${UI_BASE}//`)) {
    return null;
  }
  // Backslashes and control characters (including line breaks) never belong in a path.
  for (const character of value) {
    const code = character.codePointAt(0) ?? 0;
    if (character === "\\" || code < 0x20 || code === 0x7f) {
      return null;
    }
  }
  return value;
}

/** The path the router matches for an accepted return target, with the interface base removed. */
export function routerPathOf(target: string): string {
  const path = target.slice(UI_BASE.length);
  return path.length === 0 ? "/" : path;
}

/** The return target for a router location: the browser path of where the user is, to come back to after login. */
export function targetOf(location: RouterLocation): string {
  return `${UI_BASE}${location.pathname}${location.search}${location.hash}`;
}

/**
 * Where to go after a successful login, given the state the login page was opened with. The dashboard when there is no
 * accepted return target, and never the login page itself.
 */
export function destinationAfterLogin(state: unknown): string {
  const from = typeof state === "object" && state !== null ? (state as { from?: unknown }).from : undefined;
  const target = safeReturnTarget(from);
  if (target === null) {
    return "/dashboard";
  }
  const path = routerPathOf(target);
  return path === "/login" || path.startsWith("/login?") || path.startsWith("/login#") ? "/dashboard" : path;
}
