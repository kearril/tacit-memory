import type { Dictionary } from "../i18n/messages";
import {
  DASHBOARD_PATH,
  logDateOf,
  LOGIN_PATH,
  LOGS_PATH,
  MEMORY_PATH,
  MEMORY_VIEWS,
  promptNameOf,
  PROMPTS_PATH,
  SETTINGS_PATH,
} from "../lib/routes";

/** Browser tab title for `pathname` in the current memory language, front-loading the view name. */
export function pageTitleOf(pathname: string, dictionary: Dictionary): string {
  const parts = titlePartsOf(pathname, dictionary);
  return parts.length === 0 ? dictionary.appName : [...parts, dictionary.appName].join(" · ");
}

function titlePartsOf(pathname: string, dictionary: Dictionary): readonly string[] {
  if (pathname === LOGIN_PATH) {
    return [dictionary.login.title];
  }
  if (pathname === DASHBOARD_PATH) {
    return [dictionary.nav.dashboard];
  }
  if (pathname === SETTINGS_PATH) {
    return [dictionary.nav.settings];
  }
  if (pathname === PROMPTS_PATH || pathname.startsWith(`${PROMPTS_PATH}/`)) {
    const name = promptNameOf({ name: pathname.slice(PROMPTS_PATH.length + 1) });
    return [name === undefined ? dictionary.nav.prompts : dictionary.prompts.documents[name].title];
  }
  if (pathname === LOGS_PATH || pathname.startsWith(`${LOGS_PATH}/`)) {
    const date = logDateOf({ date: pathname.slice(LOGS_PATH.length + 1) });
    return date === undefined ? [dictionary.nav.logs] : [date, dictionary.nav.logs];
  }
  if (pathname === MEMORY_PATH || pathname.startsWith(`${MEMORY_PATH}/`)) {
    return memoryTitlePartsOf(pathname, dictionary);
  }
  return [];
}

function memoryTitlePartsOf(pathname: string, dictionary: Dictionary): readonly string[] {
  const segments = pathname.slice(MEMORY_PATH.length).split("/").filter(Boolean);
  const view = MEMORY_VIEWS.find((candidate) => candidate === segments[0]);
  if (view === undefined) {
    return [dictionary.nav.memory];
  }
  const viewLabel = dictionary.memory.views[view];
  if (view === "workstreams" && segments[1] !== undefined) {
    return [decodeSegment(segments[1]), viewLabel];
  }
  if (view === "pages" && segments[1] !== undefined && segments[2] !== undefined) {
    return [`${decodeSegment(segments[1])}/${decodeSegment(segments[2])}`, viewLabel];
  }
  return [viewLabel];
}

function decodeSegment(segment: string): string {
  try {
    return decodeURIComponent(segment);
  } catch {
    return segment;
  }
}
