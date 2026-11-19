import type { ServiceLogLevel } from "../api/logs";
import type { PageType } from "../api/memory";
import type { PromptName } from "../api/prompts";

// Every address of the interface is built and read here: the router takes its paths from these, and a link to another
// page is always built by one of these functions, so a change to an address shape is a change to this module. An address
// with a parameter in its path has a route pattern, which the router matches, and a reader of the parameters it gives.

/** The parameters the router reads from an address's path, by the names its route pattern gives them. */
export type RouteParams = Readonly<Record<string, string | undefined>>;

/** Whether a value is a calendar date written `yyyy-MM-dd`. */
export function isCalendarDate(value: string): boolean {
  if (!/^\d{4}-\d{2}-\d{2}$/u.test(value)) {
    return false;
  }
  const date = new Date(`${value}T00:00:00Z`);
  return !Number.isNaN(date.getTime()) && date.toISOString().startsWith(value);
}

/** The sign-in page. */
export const LOGIN_PATH = "/login";

/** The dashboard, which `/` and any unknown address open. */
export const DASHBOARD_PATH = "/dashboard";

/** The anchor of the dashboard's backup card. */
export const BACKUPS_ANCHOR = "backups";

/** The address of the dashboard, at its backup card when `backups` is set. */
export function dashboardPathOf(anchor?: typeof BACKUPS_ANCHOR): string {
  return anchor === undefined ? DASHBOARD_PATH : `${DASHBOARD_PATH}#${anchor}`;
}

// The settings page.

/** The settings page. */
export const SETTINGS_PATH = "/settings";

/** The sections of the settings page, in the order the page and the sidebar list them. */
export const SECTIONS = [
  "machine",
  "models",
  "bindings",
  "judgment",
  "consolidation",
  "backup",
  "sync",
  "access",
  "advanced",
] as const;

/** A section of the settings page. */
export type SectionId = (typeof SECTIONS)[number];

/** The section's anchor on the page. */
export function sectionIdOf(section: SectionId): string {
  return `settings-${section}`;
}

/** The address of the settings page, at a section when one is given. */
export function settingsPathOf(section?: SectionId): string {
  return section === undefined ? SETTINGS_PATH : `${SETTINGS_PATH}#${sectionIdOf(section)}`;
}

// The prompts page.

/** The prompts page, which opens the default document. */
export const PROMPTS_PATH = "/prompts";

/** The four prompt documents in the order the sidebar lists them, which is the order the service gives them. */
export const PROMPT_NAMES = ["instructions", "rules", "resident", "skill"] as const satisfies readonly PromptName[];

/** The document `/prompts` opens. */
export const DEFAULT_PROMPT: PromptName = "instructions";

/** The route of a document's page. */
export const PROMPT_ROUTE = `${PROMPTS_PATH}/:name`;

/** The path of a document's page. */
export function promptPathOf(name: PromptName): string {
  return `${PROMPTS_PATH}/${name}`;
}

/** The document a page address names; `undefined` for anything else, which the page sends on to the default document. */
export function promptNameOf(params: RouteParams): PromptName | undefined {
  return PROMPT_NAMES.find((name) => name === params.name);
}

// The logs page.

/** The address of the logs page; it shows today. */
export const LOGS_PATH = "/logs";

/** The route of a day's log other than today's. */
export const LOG_ROUTE = `${LOGS_PATH}/:date`;

/** The day an address names, when it is a calendar date written `yyyy-MM-dd`. */
export function logDateOf(params: RouteParams): string | undefined {
  return params.date !== undefined && isCalendarDate(params.date) ? params.date : undefined;
}

/** The address of a day's log: today is the page's own address, any other day is named in it. */
export function logPathOf(date: string, today: string): string {
  return date === today ? LOGS_PATH : `${LOGS_PATH}/${date}`;
}

/** The three levels, in the order the switches show them. */
export const LEVELS = ["information", "warning", "error"] as const satisfies readonly ServiceLogLevel[];

/** Every level: what the page shows when the address chooses none. */
export const ALL_LEVELS: ReadonlySet<ServiceLogLevel> = new Set(LEVELS);

/** What the page shows of a day: the levels switched on, and the keyword typed, as written. */
export interface LogFilters {
  readonly levels: ReadonlySet<ServiceLogLevel>;
  readonly query: string;
}

/** The filters an address carries: `level` lists the levels shown, `q` is the keyword. Unknown levels are ignored. */
export function filtersOf(search: URLSearchParams): LogFilters {
  const named = (search.get("level") ?? "").split(",");
  const levels = LEVELS.filter((level) => named.includes(level));
  return { levels: levels.length === 0 ? ALL_LEVELS : new Set(levels), query: search.get("q") ?? "" };
}

/** The query string of the filters, starting with `?`, or empty when they are the defaults. */
export function searchOf(filters: LogFilters): string {
  const search = new URLSearchParams();
  if (filters.levels.size < LEVELS.length) {
    search.set("level", LEVELS.filter((level) => filters.levels.has(level)).join(","));
  }
  if (filters.query !== "") {
    search.set("q", filters.query);
  }
  const text = search.toString();
  return text === "" ? "" : `?${text}`;
}

/** The address of today's log with the filters, for links from the other pages. */
export function logsPathOf(filters: LogFilters): string {
  return `${LOGS_PATH}${searchOf(filters)}`;
}

// The memory page.

/** The five views of the memory page, in the order the sidebar lists them; each is a segment of the address. */
export const MEMORY_VIEWS = ["persona", "workstreams", "pages", "activity", "search"] as const;
export type MemoryView = (typeof MEMORY_VIEWS)[number];

/** Where the memory page lives; `/memory` alone opens the persona. */
export const MEMORY_PATH = "/memory";

/** The address of one view. */
export function memoryPathOf(view: MemoryView): string {
  return `${MEMORY_PATH}/${view}`;
}

/** The anchor of the candidates card on the persona view, which the dashboard links to. */
export const CANDIDATES_ANCHOR = "candidates";

/** The address parameter that names the scope the persona view is filtered by. */
const SCOPE_PARAM = "scope";

/** The scope an address names, as written; `null` for none. Whether the persona has it is the view's to check. */
export function scopeParamOf(params: URLSearchParams): string | null {
  return params.get(SCOPE_PARAM);
}

/** The address parameters for a scope; none for the whole persona. */
export function scopeParams(key: string | null): Record<string, string> {
  return key === null ? {} : { [SCOPE_PARAM]: key };
}

/** The address of the persona view, at its candidates card when `candidates` is set. */
export function personaPathOf(anchor?: typeof CANDIDATES_ANCHOR): string {
  const path = memoryPathOf("persona");
  return anchor === undefined ? path : `${path}#${anchor}`;
}

/** The route of one workstream's brief. */
export const WORKSTREAM_ROUTE = `${memoryPathOf("workstreams")}/:name`;

/** The address of one workstream's brief. */
export function workstreamPathOf(name: string): string {
  return `${memoryPathOf("workstreams")}/${encodeURIComponent(name)}`;
}

/** The workstream whose brief an address opens; `null` on the list of every workstream. */
export function workstreamNameOf(params: RouteParams): string | null {
  return params.name ?? null;
}

/** The page types, in the order the filter lists them. */
export const PAGE_TYPES = ["incident", "decision", "entity", "work"] as const satisfies readonly PageType[];

/** The list's filter, as the address holds it; an empty value filters nothing. */
export interface PageFilter {
  readonly workstream: string;
  readonly type: PageType | "";
  readonly q: string;
}

/** The filter the address holds; a type that is not one of the four is left out. */
export function pageFilterOf(params: URLSearchParams): PageFilter {
  const type = params.get("type") ?? "";
  return {
    workstream: params.get("workstream") ?? "",
    type: (PAGE_TYPES as readonly string[]).includes(type) ? (type as PageType) : "",
    q: params.get("q") ?? "",
  };
}

/** The address of a filter: only what is set. */
export function pageFilterParams(filter: PageFilter): URLSearchParams {
  const params = new URLSearchParams();
  for (const key of ["workstream", "type", "q"] as const) {
    if (filter[key] !== "") {
      params.set(key, filter[key]);
    }
  }
  return params;
}

/** The address of the page list with a filter; what the filter leaves out filters nothing. */
export function pagesPathOf(filter: Partial<PageFilter> = {}): string {
  const params = pageFilterParams({ workstream: "", type: "", q: "", ...filter }).toString();
  return params === "" ? memoryPathOf("pages") : `${memoryPathOf("pages")}?${params}`;
}

/** The anchor of a page's section, which the table of contents and the search results lead to. */
export function sectionAnchor(name: string): string {
  return `section-${name}`;
}

/** The link to a page's section from the page itself. */
export function sectionHash(name: string): string {
  return `#${encodeURIComponent(sectionAnchor(name))}`;
}

/** The route of one page. */
export const PAGE_ROUTE = `${memoryPathOf("pages")}/:workstream/:slug`;

/** The address of a page by its id (`workstream/slug`), at one of its sections when `section` names it. */
export function pagePathOf(id: string, section?: string): string {
  const path = `${memoryPathOf("pages")}/${id.split("/").map(encodeURIComponent).join("/")}`;
  return section === undefined ? path : `${path}${sectionHash(section)}`;
}

/** The page an address opens: its id, the workstream it is in and its slug. */
export function pageOf(params: RouteParams): {
  readonly id: string;
  readonly workstream: string;
  readonly slug: string;
} {
  const { workstream = "", slug = "" } = params;
  return { id: `${workstream}/${slug}`, workstream, slug };
}

/** What the activity view shows, as its address holds it. */
export interface ActivityQuery {
  /** The month, `yyyy-MM`. */
  readonly month: string;
  /** The day to go to, `yyyy-MM-dd`; `null` for none. */
  readonly day: string | null;
  /** The workstream to keep; `null` for every one. */
  readonly workstream: string | null;
}

const MONTH = /^\d{4}-(0[1-9]|1[0-2])$/u;

/**
 * The activity view's query from its address: `month`, `day` and `workstream`. A month that is not `yyyy-MM` falls back
 * to the month of a valid day, or else to `current`.
 */
export function activityQueryOf(search: URLSearchParams, current: string): ActivityQuery {
  const day = search.get("day");
  const validDay = day !== null && isCalendarDate(day) ? day : null;
  const month = search.get("month");
  return {
    month: month !== null && MONTH.test(month) ? month : (validDay?.slice(0, 7) ?? current),
    day: validDay,
    workstream: search.get("workstream") || null,
  };
}

/** The address of a query, with only what is set; without a month the view opens on the current one. */
export function activityParams(query: Partial<ActivityQuery>): URLSearchParams {
  const params = new URLSearchParams();
  if (query.month !== undefined) params.set("month", query.month);
  if (query.day != null) params.set("day", query.day);
  if (query.workstream != null) params.set("workstream", query.workstream);
  return params;
}

/** The address of the activity view showing a query, for links from the other pages. */
export function activityPathOf(query: Partial<ActivityQuery>): string {
  const params = activityParams(query).toString();
  return params === "" ? memoryPathOf("activity") : `${memoryPathOf("activity")}?${params}`;
}

/** The anchor of a day on the timeline. */
export function dayAnchor(date: string): string {
  return `day-${date}`;
}

/** The search view's conditions as typed; an empty string means left out. They live in the address. */
export interface SearchFields {
  readonly query: string;
  readonly workstream: string;
  readonly from: string;
  readonly to: string;
  readonly kind: "" | "pages" | "activities";
  readonly minRerankScore: string;
  readonly topK: string;
  readonly candidates: string;
}

export const EMPTY_SEARCH: SearchFields = {
  query: "",
  workstream: "",
  from: "",
  to: "",
  kind: "",
  minRerankScore: "",
  topK: "",
  candidates: "",
};

/** Each field's name in the address. */
const SEARCH_PARAMS: Readonly<Record<keyof SearchFields, string>> = {
  query: "q",
  workstream: "workstream",
  from: "from",
  to: "to",
  kind: "kind",
  minRerankScore: "min",
  topK: "topK",
  candidates: "candidates",
};

const COUNT = /^[1-9]\d*$/;
const SCORE = /^\d+(\.\d+)?$/;

/** The conditions an address holds. A value the view does not know (a kind, a date, a number) is ignored. */
export function searchFieldsOf(params: URLSearchParams): SearchFields {
  const read = (key: keyof SearchFields, valid: (value: string) => boolean = () => true) => {
    const value = params.get(SEARCH_PARAMS[key])?.trim() ?? "";
    return valid(value) ? value : "";
  };
  const kind = read("kind");
  return {
    // The query is kept as typed: recall embeds it as given, so a space changes its vector.
    query: params.get(SEARCH_PARAMS.query) ?? "",
    workstream: read("workstream"),
    from: read("from", isCalendarDate),
    to: read("to", isCalendarDate),
    kind: kind === "pages" || kind === "activities" ? kind : "",
    minRerankScore: read("minRerankScore", (value) => SCORE.test(value)),
    topK: read("topK", (value) => COUNT.test(value)),
    candidates: read("candidates", (value) => COUNT.test(value)),
  };
}

/** The address of the conditions: only those that are set, in a fixed order. */
export function searchParamsOf(fields: SearchFields): URLSearchParams {
  const params = new URLSearchParams();
  for (const key of Object.keys(SEARCH_PARAMS) as (keyof SearchFields)[]) {
    const value = fields[key];
    if (value.trim() !== "") {
      params.set(SEARCH_PARAMS[key], key === "query" ? value : value.trim());
    }
  }
  return params;
}

/** A route parsed from a pathname, for shell features that need to know which view an address opens. */
export type ParsedRoute =
  | { readonly kind: "login" }
  | { readonly kind: "dashboard" }
  | { readonly kind: "settings" }
  | { readonly kind: "prompts"; readonly name: PromptName | undefined }
  | { readonly kind: "logs"; readonly date: string | undefined }
  | { readonly kind: "memory"; readonly view: MemoryView | undefined; readonly target?: string }
  | { readonly kind: "unknown" };

/** Parses `pathname` into the view it opens, decoding path segments safely. */
export function routeOf(pathname: string): ParsedRoute {
  if (pathname === LOGIN_PATH) {
    return { kind: "login" };
  }
  if (pathname === DASHBOARD_PATH) {
    return { kind: "dashboard" };
  }
  if (pathname === SETTINGS_PATH) {
    return { kind: "settings" };
  }
  if (pathname === PROMPTS_PATH || pathname.startsWith(`${PROMPTS_PATH}/`)) {
    return { kind: "prompts", name: promptNameOf({ name: pathname.slice(PROMPTS_PATH.length + 1) }) };
  }
  if (pathname === LOGS_PATH || pathname.startsWith(`${LOGS_PATH}/`)) {
    return { kind: "logs", date: logDateOf({ date: pathname.slice(LOGS_PATH.length + 1) }) };
  }
  if (pathname === MEMORY_PATH || pathname.startsWith(`${MEMORY_PATH}/`)) {
    const segments = pathname.slice(MEMORY_PATH.length).split("/").filter(Boolean);
    const view = MEMORY_VIEWS.find((candidate) => candidate === segments[0]);
    if (view === "workstreams" && segments[1] !== undefined) {
      return { kind: "memory", view, target: decodeSegment(segments[1]) };
    }
    if (view === "pages" && segments[1] !== undefined && segments[2] !== undefined) {
      return { kind: "memory", view, target: `${decodeSegment(segments[1])}/${decodeSegment(segments[2])}` };
    }
    return { kind: "memory", view };
  }
  return { kind: "unknown" };
}

function decodeSegment(segment: string): string {
  try {
    return decodeURIComponent(segment);
  } catch {
    return segment;
  }
}
