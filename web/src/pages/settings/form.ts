import type { ConfigFile, ConfigProblem, ConfigView } from "../../api/config";

/** The sections of the settings page, in the order of their cards. */
export type SectionId = "general" | "models" | "bindings" | "judgment" | "consolidation" | "backup" | "access";

/** What an input holds: a whole number, any number, text, one of fixed choices, or on and off (`ON` or empty). */
export type FieldKind = "integer" | "number" | "text" | "choice" | "toggle";

/** What a toggle's input holds while it is on. */
export const ON = "on";

/**
 * A setting the page edits: a value in `config.yaml` by its key path, the section whose card holds it, and what its input
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

/** The settings of the consolidation card, all whole numbers. */
export const CONSOLIDATION = [
  "remindAfterDays",
  "remindCandidates",
  "budgetSeconds",
  "maxConcurrency",
  "hintItemLength",
  "hintResidentLength",
] as const;

/** The four judgments, each with a pair of thresholds. */
export const JUDGMENTS = ["samePreference", "samePage", "sameTopic", "ruleConflict"] as const;

/** The fields the page edits, in the order they appear on it. */
export const FIELDS: readonly Field[] = [
  { path: "server.port", section: "general", kind: "integer" },
  { path: "server.host", section: "general", kind: "text" },
  { path: "limits.brief", section: "general", kind: "integer" },
  { path: "limits.page.minAliases", section: "general", kind: "integer" },
  ...MODELS.flatMap((model) => {
    const within = `models.${model}`;
    return [
      { path: within, section: "models" as const, kind: "toggle" as const },
      { path: `${within}.provider`, section: "models" as const, kind: "choice" as const, within },
      { path: `${within}.url`, section: "models" as const, kind: "text" as const, within },
      { path: `${within}.model`, section: "models" as const, kind: "text" as const, within },
    ];
  }),
  { path: "retrieval.candidates", section: "judgment", kind: "integer" },
  { path: "retrieval.topK", section: "judgment", kind: "integer" },
  { path: "retrieval.minRerankScore", section: "judgment", kind: "number" },
  ...JUDGMENTS.flatMap((judgment) =>
    (["certain", "unlikely"] as const).map((bound) => ({
      path: `decision.${judgment}.${bound}`,
      section: "judgment" as const,
      kind: "number" as const,
      group: `decision.${judgment}`,
    })),
  ),
  ...CONSOLIDATION.map((key) => ({ path: `consolidation.${key}`, section: "consolidation" as const, kind: "integer" as const })),
  { path: "backup.keep", section: "backup", kind: "integer" },
  { path: "backup.webdav.url", section: "backup", kind: "text" },
  { path: "backup.webdav.username", section: "backup", kind: "text" },
];

/** The secrets, by key path, and the section whose card holds each. They are changed apart from the other fields. */
export const SECRETS: readonly { readonly path: string; readonly section: SectionId }[] = [
  { path: "models.embedding.apiKey", section: "models" },
  { path: "models.reranker.apiKey", section: "models" },
  { path: "models.decision.apiKey", section: "models" },
  { path: "backup.webdav.password", section: "backup" },
];

/**
 * The secrets changed on the page, by key path: a text is the new value to write into `config.yaml`, `null` removes the
 * secret from the file. A secret not named keeps what the file holds.
 */
export type SecretEdits = Readonly<Record<string, string | null>>;

/** The settings the page shows without letting them change, and the section that shows them. */
export const READ_ONLY: readonly { readonly path: string; readonly section: SectionId }[] = [
  { path: "memory.path", section: "general" },
  { path: "language", section: "general" },
];

/** What each field's input holds, by key path: the text as typed, so a half-typed number is kept as it is. */
export type Drafts = Readonly<Record<string, string>>;

/** The inputs as the view gives them: the file's value, or the default where the file leaves the setting out. */
export function draftsOf(view: ConfigView): Drafts {
  return Object.fromEntries(
    FIELDS.map((field) => [
      field.path,
      field.kind === "toggle" ? (present(view.file, field.path) ? ON : "") : textOf(effective(view, "file", field.path)),
    ]),
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

/** The fields whose input no longer holds the value the view gave, then the secrets changed, in the page's order. */
export function changedPaths(view: ConfigView, drafts: Drafts, secrets: SecretEdits = {}): string[] {
  const original = draftsOf(view);
  return [
    ...FIELDS.filter((field) => !isOff(drafts, field.path))
      .filter((field) => !sameInput(drafts[field.path] ?? "", original[field.path] ?? "", field.kind))
      .map((field) => field.path),
    ...SECRETS.filter((secret) => secret.path in secrets && !isOff(drafts, secret.path)).map((secret) => secret.path),
  ];
}

/** Whether the setting at `path`, or either threshold of the pair at `path`, holds something other than its default. */
export function differsFromDefault(view: ConfigView, drafts: Drafts, path: string): boolean {
  return fieldsAt(path).some(
    (field) => !sameInput(drafts[field.path] ?? "", textOf(valueAt(view.defaults, field.path)), field.kind),
  );
}

/**
 * The config to save: the file as the view gave it, with each field taken from its input. A setting equal to its default
 * is left out (`null`), so the file follows the default from then on; a pair of thresholds is written whole once either
 * differs from its default. An emptied input is the default. A cloud backup with neither an address nor a user name is
 * no cloud backup. The secrets go apart, only those changed. `invalid` names the inputs that do not hold a number (or a
 * whole number where one is needed) and the secrets being rewritten to nothing; a config with any is not to be sent.
 */
export function bodyOf(
  view: ConfigView,
  drafts: Drafts,
  secrets: SecretEdits = {},
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
  const sent = Object.fromEntries(Object.entries(secrets).filter(([path]) => !isOff(drafts, path)));
  for (const secret of SECRETS) {
    if (sent[secret.path]?.trim() === "") {
      invalid.push(secret.path);
    }
  }
  return { config, secrets: sent, invalid };
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
  /** How many problems each section's card holds. */
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
export function placeProblems(problems: readonly ConfigProblem[]): PlacedProblems {
  const byField: Record<string, string[]> = {};
  const bySection: Partial<Record<SectionId, number>> = {};
  const unplaced: ConfigProblem[] = [];
  for (const problem of problems) {
    const section =
      FIELDS.find((field) => field.path === problem.path || field.group === problem.path)?.section ??
      [...READ_ONLY, ...SECRETS].find((item) => item.path === problem.path)?.section;
    if (section === undefined) {
      unplaced.push(problem);
      continue;
    }
    (byField[problem.path] ??= []).push(problem.message);
    bySection[section] = (bySection[section] ?? 0) + 1;
  }
  const order = [
    ...READ_ONLY.map((item) => item.path),
    ...FIELDS.flatMap((field) => [field.group, field.path]),
    ...SECRETS.map((secret) => secret.path),
  ];
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

/** The section card's anchor on the page. */
export function sectionIdOf(section: SectionId): string {
  return `settings-${section}`;
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
