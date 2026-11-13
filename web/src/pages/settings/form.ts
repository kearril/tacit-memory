import type { ConfigFile, ConfigProblem, ConfigView } from "../../api/config";

/** The sections of the settings page, in the order of their cards. */
export type SectionId = "general" | "models" | "bindings" | "judgment" | "consolidation" | "backup" | "access";

/**
 * A setting the page edits: a number in `config.yaml` by its key path, and the section whose card holds it. A threshold
 * belongs to a pair (`group`), which the file holds whole or not at all.
 */
export interface Field {
  readonly path: string;
  readonly section: SectionId;
  readonly integer: boolean;
  readonly group?: string;
}

/** The four judgments, each with a pair of thresholds. */
export const JUDGMENTS = ["samePreference", "samePage", "sameTopic", "ruleConflict"] as const;

/** The fields the page edits, in the order they appear on it. */
export const FIELDS: readonly Field[] = [
  { path: "retrieval.candidates", section: "judgment", integer: true },
  { path: "retrieval.topK", section: "judgment", integer: true },
  { path: "retrieval.minRerankScore", section: "judgment", integer: false },
  ...JUDGMENTS.flatMap((judgment) =>
    (["certain", "unlikely"] as const).map((bound) => ({
      path: `decision.${judgment}.${bound}`,
      section: "judgment" as const,
      integer: false,
      group: `decision.${judgment}`,
    })),
  ),
];

/** What each field's input holds, by key path: the text as typed, so a half-typed number is kept as it is. */
export type Drafts = Readonly<Record<string, string>>;

/** The inputs as the view gives them: the file's value, or the default where the file leaves the setting out. */
export function draftsOf(view: ConfigView): Drafts {
  return Object.fromEntries(
    FIELDS.map((field) => [field.path, textOf(valueAt(view.file, field.path) ?? valueAt(view.defaults, field.path))]),
  );
}

/** The drafts with the input at `path` changed to `text`. */
export function edit(drafts: Drafts, path: string, text: string): Drafts {
  return { ...drafts, [path]: text };
}

/** The drafts with the setting at `path`, or both thresholds of the pair at `path`, set back to the default. */
export function restoreDefault(view: ConfigView, drafts: Drafts, path: string): Drafts {
  const restored = { ...drafts };
  for (const field of fieldsAt(path)) {
    restored[field.path] = textOf(valueAt(view.defaults, field.path));
  }
  return restored;
}

/** The fields whose input no longer holds the number the view gave, in the page's order. */
export function changedPaths(view: ConfigView, drafts: Drafts): string[] {
  const original = draftsOf(view);
  return FIELDS.filter((field) => !sameNumber(drafts[field.path] ?? "", original[field.path] ?? "")).map(
    (field) => field.path,
  );
}

/** Whether the setting at `path`, or either threshold of the pair at `path`, holds something other than its default. */
export function differsFromDefault(view: ConfigView, drafts: Drafts, path: string): boolean {
  return fieldsAt(path).some(
    (field) => !sameNumber(drafts[field.path] ?? "", textOf(valueAt(view.defaults, field.path))),
  );
}

/**
 * The config to save: the file as the view gave it, with each field taken from its input. A setting equal to its default
 * is left out (`null`), so the file follows the default from then on; a pair of thresholds is written whole once either
 * differs from its default. An emptied input is the default. `invalid` names the inputs that do not hold a number (or a
 * whole number where one is needed); a config with any is not to be sent.
 */
export function bodyOf(view: ConfigView, drafts: Drafts): { readonly config: ConfigFile; readonly invalid: string[] } {
  const invalid: string[] = [];
  const numbers = new Map<string, number | null>();
  for (const field of FIELDS) {
    const fallback = valueAt(view.defaults, field.path);
    const value = numberOf(drafts[field.path] ?? "", field.integer);
    if (value === undefined) {
      invalid.push(field.path);
    }
    numbers.set(field.path, value ?? fallback);
  }

  let config: ConfigFile = structuredClone(view.file);
  for (const field of FIELDS.filter((field) => field.group === undefined)) {
    const value = numbers.get(field.path) ?? null;
    config = setAt(config, field.path, value === valueAt(view.defaults, field.path) ? null : value);
  }
  for (const group of new Set(FIELDS.flatMap((field) => (field.group === undefined ? [] : [field.group])))) {
    const pair = fieldsAt(group);
    const asDefault = pair.every((field) => numbers.get(field.path) === valueAt(view.defaults, field.path));
    config = setAt(
      config,
      group,
      asDefault ? null : Object.fromEntries(pair.map((field) => [lastKey(field.path), numbers.get(field.path) ?? null])),
    );
  }
  return { config, invalid };
}

/** The service's problems with a config, placed on the page. */
export interface PlacedProblems {
  /** The messages by the field or pair of thresholds they are about. */
  readonly byField: Readonly<Record<string, string[]>>;
  /** How many problems each section's card holds. */
  readonly bySection: Partial<Record<SectionId, number>>;
  /** The problems no field on the page answers to, in the order given. */
  readonly unplaced: readonly ConfigProblem[];
  /** The field or pair of the first placed problem in the page's order, to scroll to; `null` when none is placed. */
  readonly first: string | null;
}

/**
 * Places each problem on the field whose key path it names, or on the pair of thresholds it names; anything else (another
 * section, a key the page does not show) stays unplaced.
 */
export function placeProblems(problems: readonly ConfigProblem[]): PlacedProblems {
  const byField: Record<string, string[]> = {};
  const bySection: Partial<Record<SectionId, number>> = {};
  const unplaced: ConfigProblem[] = [];
  for (const problem of problems) {
    const field = FIELDS.find((field) => field.path === problem.path || field.group === problem.path);
    if (field === undefined) {
      unplaced.push(problem);
      continue;
    }
    (byField[problem.path] ??= []).push(problem.message);
    bySection[field.section] = (bySection[field.section] ?? 0) + 1;
  }
  const first = FIELDS.flatMap((field) => [field.group, field.path]).find(
    (path) => path !== undefined && byField[path] !== undefined,
  );
  return { byField, bySection, unplaced, first: first ?? null };
}

/** The fields at `path`: the one field, or both thresholds of a pair. */
export function fieldsAt(path: string): readonly Field[] {
  return FIELDS.filter((field) => field.path === path || field.group === path);
}

/** The id of the input of the setting at `path`; a pair of thresholds is reached through its first input. */
export function inputIdOf(path: string): string {
  const field = fieldsAt(path)[0];
  return `setting-${(field?.path ?? path).replaceAll(".", "-")}`;
}

/** The section card's anchor on the page. */
export function sectionIdOf(section: SectionId): string {
  return `settings-${section}`;
}

/** The number at a key path of the config, or `null` where any part of the path is missing. */
export function valueAt(file: ConfigFile, path: string): number | null {
  let node: unknown = file;
  for (const key of path.split(".")) {
    if (typeof node !== "object" || node === null) {
      return null;
    }
    node = (node as Record<string, unknown>)[key];
  }
  return typeof node === "number" ? node : null;
}

function setAt(file: ConfigFile, path: string, value: unknown): ConfigFile {
  const keys = path.split(".");
  const root = file as unknown as Record<string, unknown>;
  let node = root;
  for (const key of keys.slice(0, -1)) {
    const next = node[key];
    node = node[key] = typeof next === "object" && next !== null ? (next as Record<string, unknown>) : {};
  }
  node[lastKey(path)] = value;
  return file;
}

function lastKey(path: string): string {
  return path.slice(path.lastIndexOf(".") + 1);
}

function textOf(value: number | null): string {
  return value === null ? "" : String(value);
}

/** The number an input holds: `null` when it is empty (the default), `undefined` when it is not a valid number. */
function numberOf(text: string, integer: boolean): number | null | undefined {
  const trimmed = text.trim();
  if (trimmed === "") {
    return null;
  }
  const value = Number(trimmed);
  return Number.isFinite(value) && (!integer || Number.isInteger(value)) ? value : undefined;
}

function sameNumber(a: string, b: string): boolean {
  const x = numberOf(a, false);
  const y = numberOf(b, false);
  return x === undefined || y === undefined ? a.trim() === b.trim() : x === y;
}
