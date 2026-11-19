import type { ConfigFile, ConfigKeyPath, ConfigProblem, ConfigView } from "../../api/config";
import type { SectionId } from "../../lib/routes";

/** What an input holds: a whole number, any number, text, one of fixed choices, or on and off (`ON` or empty). */
export type FieldKind = "integer" | "number" | "text" | "choice" | "toggle";

/** What a toggle's input holds while it is on. */
export const ON = "on";

/**
 * A setting the page edits: a value in `config.yaml` by its key path, the section that holds it, and what its input
 * takes. A threshold belongs to a pair (`group`), which the file holds whole or not at all.
 */
export interface Field {
  readonly path: string;
  readonly section: SectionId;
  readonly kind: FieldKind;
  readonly group?: string;
  /** The toggle this field belongs to: while it is off, the field is kept on the page but left out of the config. */
  readonly within?: string;
}

/** A value of `config.yaml`. */
export type Value = number | string | null;

/** The three auxiliary models. */
export const MODELS = ["embedding", "reranker", "decision"] as const;

/** The consolidation settings, all whole numbers. */
export const CONSOLIDATION = [
  "remindAfterDays",
  "remindCandidates",
  "budgetSeconds",
  "maxConcurrency",
  "hintEntryLength",
  "hintResidentLength",
] as const;

/** The four judgments, each with a pair of thresholds. */
export const JUDGMENTS = ["samePreference", "samePage", "sameTopic", "ruleConflict"] as const;

/**
 * How the page shows each key of `config.yaml`: the section that holds it and, for a key edited with the other fields, its
 * input, the pair of thresholds it belongs to and the toggle it sits under. A secret or a read-only key has no entry in
 * `FIELDS`; the page shows it apart (a secret's input still holds its draft). Every key the service describes needs an
 * entry, so a new key does not compile until it has one.
 */
export interface Presentation {
  readonly section: SectionId;
  readonly kind?: Exclude<FieldKind, "toggle">;
  readonly group?: string;
  readonly within?: string;
}

const model = (model: (typeof MODELS)[number]) => `models.${model}`;

/** Each key's place on the page; the keys edited with the other fields are in the order they appear on it. */
export const PRESENTATION: Readonly<Record<ConfigKeyPath, Presentation>> = {
  "server.port": { section: "advanced", kind: "integer" },
  "server.host": { section: "access", kind: "text" },
  "memory.path": { section: "advanced" },
  language: { section: "advanced" },
  "machine.name": { section: "machine", kind: "text" },
  "machine.description": { section: "machine", kind: "text" },
  "limits.brief": { section: "advanced", kind: "integer" },
  "limits.page.minAliases": { section: "advanced", kind: "integer" },
  "models.embedding.provider": { section: "models", kind: "choice", within: model("embedding") },
  "models.embedding.url": { section: "models", kind: "text", within: model("embedding") },
  "models.embedding.model": { section: "models", kind: "text", within: model("embedding") },
  "models.embedding.apiKey": { section: "models" },
  "models.reranker.provider": { section: "models", kind: "choice", within: model("reranker") },
  "models.reranker.url": { section: "models", kind: "text", within: model("reranker") },
  "models.reranker.model": { section: "models", kind: "text", within: model("reranker") },
  "models.reranker.apiKey": { section: "models" },
  "models.decision.provider": { section: "models", kind: "choice", within: model("decision") },
  "models.decision.url": { section: "models", kind: "text", within: model("decision") },
  "models.decision.model": { section: "models", kind: "text", within: model("decision") },
  "models.decision.apiKey": { section: "models" },
  "retrieval.candidates": { section: "judgment", kind: "integer" },
  "retrieval.topK": { section: "judgment", kind: "integer" },
  "retrieval.minRerankScore": { section: "judgment", kind: "number" },
  "decision.samePreference.certain": { section: "judgment", kind: "number", group: "decision.samePreference" },
  "decision.samePreference.unlikely": { section: "judgment", kind: "number", group: "decision.samePreference" },
  "decision.samePage.certain": { section: "judgment", kind: "number", group: "decision.samePage" },
  "decision.samePage.unlikely": { section: "judgment", kind: "number", group: "decision.samePage" },
  "decision.sameTopic.certain": { section: "judgment", kind: "number", group: "decision.sameTopic" },
  "decision.sameTopic.unlikely": { section: "judgment", kind: "number", group: "decision.sameTopic" },
  "decision.ruleConflict.certain": { section: "judgment", kind: "number", group: "decision.ruleConflict" },
  "decision.ruleConflict.unlikely": { section: "judgment", kind: "number", group: "decision.ruleConflict" },
  "consolidation.remindAfterDays": { section: "consolidation", kind: "integer" },
  "consolidation.remindCandidates": { section: "consolidation", kind: "integer" },
  "consolidation.budgetSeconds": { section: "consolidation", kind: "integer" },
  "consolidation.maxConcurrency": { section: "consolidation", kind: "integer" },
  "consolidation.hintEntryLength": { section: "consolidation", kind: "integer" },
  "consolidation.hintResidentLength": { section: "consolidation", kind: "integer" },
  "backup.keep": { section: "backup", kind: "integer" },
  "backup.webdav.url": { section: "backup", kind: "text" },
  "backup.webdav.username": { section: "backup", kind: "text" },
  "backup.webdav.password": { section: "backup" },
};

/** The fields the page edits, in the order they appear on it: each model's toggle comes before the fields it holds. */
export const FIELDS: readonly Field[] = Object.entries(PRESENTATION).flatMap(([path, shown]): Field[] => {
  if (shown.kind === undefined) {
    return [];
  }
  const field: Field = { ...shown, path, kind: shown.kind };
  const opensToggle =
    shown.within !== undefined && Object.values(PRESENTATION).find((other) => other.within === shown.within) === shown;
  return opensToggle && shown.within !== undefined
    ? [{ path: shown.within, section: shown.section, kind: "toggle" }, field]
    : [field];
});

/** The secrets the service describes, by key path. They are saved apart from the other fields. */
function secretsOf(view: ConfigView): ConfigKeyPath[] {
  return view.keys.filter((key) => key.secret).map((key) => key.path);
}

/** The settings the service describes as read-only, by key path; the page shows them without letting them change. */
function readOnlyOf(view: ConfigView): ConfigKeyPath[] {
  return view.keys.filter((key) => key.readOnly).map((key) => key.path);
}

/**
 * The secrets to change, by key path: a text is the new value to write into `config.yaml`, `null` removes the secret
 * from the file. A secret not named keeps what the file holds.
 */
export type SecretEdits = Readonly<Record<string, string | null>>;

/**
 * What each input holds, by key path: the text as typed, so a half-typed number is kept as it is. A secret's input is
 * here too, holding the value `config.yaml` gives it until it is changed.
 */
export type Drafts = Readonly<Record<string, string>>;

/** The inputs as the view gives them: the file's value, or the default where the file leaves the setting out. */
export function draftsOf(view: ConfigView): Drafts {
  return Object.fromEntries([
    ...FIELDS.map((field) => [
      field.path,
      field.kind === "toggle"
        ? present(view.file, field.path)
          ? ON
          : ""
        : textOf(effective(view, "file", field.path)),
    ]),
    ...view.secrets.map((secret) => [secret.path, secret.value ?? ""]),
  ]);
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

/** The fields whose input no longer holds the value the view gave, then the secrets changed, in the page's order. */
export function changedPaths(view: ConfigView, drafts: Drafts): string[] {
  const original = draftsOf(view);
  return [
    ...FIELDS.filter((field) => !isOff(drafts, field.path))
      .filter((field) => !sameInput(drafts[field.path] ?? "", original[field.path] ?? "", field.kind))
      .map((field) => field.path),
    ...Object.keys(secretEdits(view, drafts)),
  ];
}

/**
 * The secrets whose input differs from what `config.yaml` holds, in the page's order: an input of blanks removes the
 * secret. One from its environment variable, or under a toggle that is off, is never changed.
 */
function secretEdits(view: ConfigView, drafts: Drafts): SecretEdits {
  const edits: Record<string, string | null> = {};
  for (const secret of view.secrets) {
    if (secret.source === "environment" || isOff(drafts, secret.path)) {
      continue;
    }
    const typed = drafts[secret.path] ?? "";
    const value = typed.trim() === "" ? null : typed;
    if (value !== (secret.value ?? null)) {
      edits[secret.path] = value;
    }
  }
  return edits;
}

/** Whether the setting at `path`, or either threshold of the pair at `path`, holds something other than its default. */
export function differsFromDefault(view: ConfigView, drafts: Drafts, path: string): boolean {
  return fieldsAt(path).some(
    (field) => !sameInput(drafts[field.path] ?? "", textOf(valueAt(view.defaults, field.path)), field.kind),
  );
}

/**
 * The limits of the number at `path` when its input holds a number outside them; `null` for one inside, for an input that
 * is empty or not a number yet, and for a setting without limits. The service checks the same limits when saving.
 */
export function outOfRange(
  view: ConfigView,
  drafts: Drafts,
  path: string,
): { readonly min: number; readonly max: number } | null {
  const key = view.keys.find((item) => item.path === path);
  const value = parse(drafts[path] ?? "", "number");
  if (key?.min == null || key.max == null || typeof value !== "number") {
    return null;
  }
  return value < key.min || value > key.max ? { min: key.min, max: key.max } : null;
}

/** How many of the settings at `paths` take effect only when the service restarts. */
export function restartsAmong(view: ConfigView, paths: readonly string[]): number {
  return paths.filter((path) => view.keys.some((key) => key.path === path && key.restart)).length;
}

/**
 * The config to save: the file as the view gave it, with each field taken from its input. A setting equal to its default
 * is left out (`null`), so the file follows the default from then on; a pair of thresholds is written whole once either
 * differs from its default. An emptied input is the default. A cloud backup with neither an address nor a user name is
 * no cloud backup. The secrets go apart, only those changed. `invalid` names the inputs that do not hold a number (or a
 * whole number where one is needed); a config with any is not to be sent.
 */
export function bodyOf(
  view: ConfigView,
  drafts: Drafts,
): { readonly config: ConfigFile; readonly secrets: SecretEdits; readonly invalid: string[] } {
  const invalid: string[] = [];
  const values = new Map<string, Value>();
  for (const field of FIELDS.filter((field) => field.kind !== "toggle" && !isOff(drafts, field.path))) {
    const value = parse(drafts[field.path] ?? "", field.kind);
    if (value === undefined) {
      invalid.push(field.path);
    }
    values.set(field.path, value ?? valueAt(view.defaults, field.path));
  }

  let config: ConfigFile = structuredClone(view.file);
  for (const field of FIELDS.filter((field) => field.group === undefined && values.has(field.path))) {
    const value = values.get(field.path) ?? null;
    config = setAt(config, field.path, value === valueAt(view.defaults, field.path) ? null : value);
  }
  for (const group of new Set(FIELDS.flatMap((field) => (field.group === undefined ? [] : [field.group])))) {
    const pair = fieldsAt(group);
    const asDefault = pair.every((field) => values.get(field.path) === valueAt(view.defaults, field.path));
    config = setAt(
      config,
      group,
      asDefault ? null : Object.fromEntries(pair.map((field) => [lastKey(field.path), values.get(field.path) ?? null])),
    );
  }
  for (const toggle of FIELDS.filter((field) => field.kind === "toggle" && drafts[field.path] !== ON)) {
    config = setAt(config, toggle.path, null);
  }
  if (valueAt(config, "backup.webdav.url") === null && valueAt(config, "backup.webdav.username") === null) {
    config = setAt(config, "backup.webdav", null);
  }
  return { config, secrets: secretEdits(view, drafts), invalid };
}

/** A setting that waits for a restart, with the value the service runs and the one `config.yaml` holds. */
export interface Pending {
  readonly path: string;
  readonly running: string;
  readonly saved: string;
}

/**
 * The settings that wait for a restart (or that only `config.yaml` can change) and whose saved value is not yet the one
 * the service runs. It follows from the file and the running service alone, so it holds after a reload of the page or on
 * another device, until the service restarts.
 */
export function pendingRestart(view: ConfigView): Pending[] {
  return view.keys
    .filter((key) => key.restart)
    .map((key) => ({
      path: key.path,
      running: textOf(effective(view, "running", key.path)),
      saved: textOf(effective(view, "file", key.path)),
    }))
    .filter((item) => item.running !== item.saved);
}

/**
 * The settings that take effect at once but whose value in `config.yaml` is not the one the service runs: the file was
 * edited by hand since the service last loaded it.
 */
export function drifted(view: ConfigView): string[] {
  return view.keys
    .filter((key) => !key.restart && !key.readOnly)
    .filter((key) => textOf(effective(view, "file", key.path)) !== textOf(effective(view, "running", key.path)))
    .map((key) => key.path);
}

/** The service's problems with a config, placed on the page. */
export interface PlacedProblems {
  /** The messages by the field or pair of thresholds they are about. */
  readonly byField: Readonly<Record<string, string[]>>;
  /** How many problems each section holds. */
  readonly bySection: Partial<Record<SectionId, number>>;
  /** The problems no field on the page answers to, in the order given. */
  readonly unplaced: readonly ConfigProblem[];
  /** The field or pair of the first placed problem in the page's order, to scroll to; `null` when none is placed. */
  readonly first: string | null;
}

/**
 * Places each problem on the field whose key path it names, on the pair of thresholds it names, or on a read-only setting
 * the page shows; anything else (a key the page does not show) stays unplaced.
 */
export function placeProblems(view: ConfigView, problems: readonly ConfigProblem[]): PlacedProblems {
  const byField: Record<string, string[]> = {};
  const bySection: Partial<Record<SectionId, number>> = {};
  const unplaced: ConfigProblem[] = [];
  for (const problem of problems) {
    const apart = [...readOnlyOf(view), ...secretsOf(view)].find((path) => path === problem.path);
    const section =
      FIELDS.find((field) => field.path === problem.path || field.group === problem.path)?.section ??
      (apart === undefined ? undefined : PRESENTATION[apart].section);
    if (section === undefined) {
      unplaced.push(problem);
      continue;
    }
    (byField[problem.path] ??= []).push(problem.message);
    bySection[section] = (bySection[section] ?? 0) + 1;
  }
  const order = [...readOnlyOf(view), ...FIELDS.flatMap((field) => [field.group, field.path]), ...secretsOf(view)];
  const first = order.find((path) => path !== undefined && byField[path] !== undefined);
  return { byField, bySection, unplaced, first: first ?? null };
}

/** Whether the setting at `path` belongs to a toggle that is off, so it stays on the page but out of the config. */
export function isOff(drafts: Drafts, path: string): boolean {
  const toggle = FIELDS.find((field) => field.kind === "toggle" && path.startsWith(`${field.path}.`));
  return toggle !== undefined && drafts[toggle.path] !== ON;
}

/** Whether the config holds anything (a mapping or a value) at `path`. */
function present(file: ConfigFile, path: string): boolean {
  let node: unknown = file;
  for (const key of path.split(".")) {
    if (typeof node !== "object" || node === null) {
      return false;
    }
    const record = node as Record<string, unknown>;
    node = record[keyIn(record, key)];
  }
  return node !== null && node !== undefined;
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

/**
 * The value at a key path of the config, or `null` where any part of the path is missing. Key paths are those of
 * `config.yaml` (`backup.webdav.url`); the keys of the JSON may differ in case (`webDav`), so each is matched without it.
 */
export function valueAt(file: ConfigFile, path: string): Value {
  let node: unknown = file;
  for (const key of path.split(".")) {
    if (typeof node !== "object" || node === null) {
      return null;
    }
    const record = node as Record<string, unknown>;
    node = record[keyIn(record, key)];
  }
  return typeof node === "number" || typeof node === "string" ? node : null;
}

/** The value the setting takes in the file or in the running service: what it holds, or else its default. */
export function effective(view: ConfigView, which: "file" | "running", path: string): Value {
  return valueAt(view[which], path) ?? valueAt(view.defaults, path);
}

/** A value as an input or a note shows it. */
export function textOf(value: Value): string {
  return value === null ? "" : String(value);
}

function setAt(file: ConfigFile, path: string, value: unknown): ConfigFile {
  const keys = path.split(".");
  let node = file as unknown as Record<string, unknown>;
  for (const key of keys.slice(0, -1)) {
    const name = keyIn(node, key);
    const next = node[name];
    node = node[name] = typeof next === "object" && next !== null ? (next as Record<string, unknown>) : {};
  }
  node[keyIn(node, lastKey(path))] = value;
  return file;
}

/** The key of `record` that is `key` but for case, or `key` itself when there is none. */
function keyIn(record: Record<string, unknown>, key: string): string {
  return Object.keys(record).find((name) => name.toLowerCase() === key.toLowerCase()) ?? key;
}

function lastKey(path: string): string {
  return path.slice(path.lastIndexOf(".") + 1);
}

/** The value an input holds: `null` when it is empty (the default), `undefined` when it is not a valid number. */
function parse(text: string, kind: FieldKind): Value | undefined {
  const trimmed = text.trim();
  if (trimmed === "") {
    return null;
  }
  if (kind !== "integer" && kind !== "number") {
    return trimmed;
  }
  const value = Number(trimmed);
  return Number.isFinite(value) && (kind === "number" || Number.isInteger(value)) ? value : undefined;
}

function sameInput(a: string, b: string, kind: FieldKind): boolean {
  const loose = kind === "integer" ? "number" : kind;
  const x = parse(a, loose);
  const y = parse(b, loose);
  return x === undefined || y === undefined ? a.trim() === b.trim() : x === y;
}
