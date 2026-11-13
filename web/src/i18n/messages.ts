import { ApiError } from "../api/client";
import type { BindingProblem } from "../api/status";
import type { components } from "../api/schema";
import type { Duration } from "../pages/dashboard/format";
import type { CONSOLIDATION, JUDGMENTS, SectionId } from "../pages/settings/form";
import { en } from "./en";
import { zh } from "./zh";

/** A setting's name and the sentence or two that explains it. */
export interface Explained {
  readonly label: string;
  readonly help: string;
}

/**
 * The text of the interface, in both memory languages. Every dictionary is annotated with this type, so a dictionary that
 * leaves a key out or adds one does not compile.
 */
export interface Dictionary {
  readonly appName: string;
  readonly mainNav: string;
  readonly nav: {
    readonly dashboard: string;
    readonly settings: string;
    readonly prompts: string;
    readonly logs: string;
    readonly memory: string;
  };
  readonly notImplemented: string;
  readonly unreachable: string;
  readonly unexpectedAnswer: string;
  readonly menuOpen: string;
  readonly menuClose: string;
  readonly logout: string;
  readonly dismiss: string;
  readonly notifications: string;
  readonly connection: {
    readonly local: string;
    readonly localNote: string;
    readonly remote: string;
    readonly remoteNote: string;
  };
  readonly showPassword: string;
  readonly hidePassword: string;
  readonly login: {
    readonly title: string;
    readonly description: string;
    readonly passwordLabel: string;
    readonly submit: string;
    readonly submitting: string;
    readonly passwordPlaceholder: string;
  };
  readonly access: {
    readonly title: string;
    readonly summary: string;
    readonly stateSet: string;
    readonly stateUnset: string;
    readonly newPassword: string;
    readonly currentPassword: string;
    readonly newPlaceholder: string;
    readonly currentPlaceholder: string;
    readonly setPassword: string;
    readonly changePassword: string;
    readonly saving: string;
    readonly saved: string;
    readonly saveFailed: string;
    readonly clearPassword: string;
    readonly clearTitle: string;
    readonly clearBody: string;
    readonly clearConfirm: string;
    readonly cancel: string;
    readonly cleared: string;
    readonly clearFailed: string;
    readonly clearBlocked: (address: string) => string;
  };
  readonly settings: {
    readonly sections: Readonly<Record<SectionId, string>>;
    readonly sectionsNav: string;
    readonly problemsIn: (count: number) => string;
    readonly loadFailed: string;
    readonly defaultValue: (value: string) => string;
    readonly range: (min: string, max: string) => string;
    readonly restoreDefault: string;
    readonly notANumber: string;
    readonly notAWholeNumber: string;
    readonly changes: (count: number) => string;
    readonly discard: string;
    readonly save: string;
    readonly saving: string;
    readonly saved: string;
    readonly saveFailed: string;
    readonly unplacedTitle: string;
    readonly reload: string;
    readonly cancel: string;
    readonly discardTitle: string;
    readonly discardBody: string;
    readonly discardConfirm: string;
    readonly leaveTitle: string;
    readonly leaveBody: string;
    readonly leaveConfirm: string;
    readonly restartBadge: string;
    readonly restartHelp: string;
    readonly pendingBadge: (running: string) => string;
    readonly readOnlyNote: string;
    readonly required: string;
    readonly restartTitle: string;
    readonly restartBody: string;
    readonly restartLine: (running: string, saved: string) => string;
    readonly restartOk: string;
    readonly driftTitle: string;
    readonly driftBody: string;
    readonly reloadTitle: string;
    readonly reloadBody: string;
    readonly reloadConfirm: string;
    readonly secret: {
      readonly hidden: string;
      readonly unset: string;
      readonly show: string;
      readonly hide: string;
      readonly rewrite: string;
      readonly clear: string;
      readonly undo: string;
      readonly fromEnvironment: (variable: string) => string;
      readonly fromFile: string;
      readonly environmentNote: (variable: string) => string;
      readonly fileNote: (variable: string) => string;
      readonly willClear: string;
      readonly newValue: string;
      readonly emptyRewrite: string;
    };
    readonly consolidation: {
      readonly title: string;
      readonly summary: string;
      readonly fields: Readonly<Record<(typeof CONSOLIDATION)[number], Explained>>;
    };
    readonly models: {
      readonly title: string;
      readonly summary: string;
      readonly enable: (model: string) => string;
      readonly off: string;
      readonly providers: Readonly<Record<"generic" | "cloudflare", string>>;
      readonly fields: Readonly<Record<"provider" | "url" | "model" | "apiKey", Explained>>;
      readonly probeDirty: string;
    };
    readonly backup: {
      readonly title: string;
      readonly summary: string;
      readonly local: string;
      readonly cloud: string;
      readonly urlDefault: string;
      readonly usernameDefault: string;
      readonly fields: Readonly<Record<"keep" | "url" | "username" | "password", Explained>>;
    };
    readonly general: {
      readonly title: string;
      readonly summary: string;
      readonly service: string;
      readonly memory: string;
      readonly hostNeedsPassword: string;
      readonly hostDefault: string;
      readonly languages: Readonly<Record<string, string>>;
      readonly fields: Readonly<
        Record<"port" | "host" | "memoryPath" | "language" | "brief" | "minAliases", Explained>
      >;
    };
    readonly judgment: {
      readonly title: string;
      readonly summary: string;
      readonly calibration: string;
      readonly retrieval: string;
      readonly thresholds: string;
      readonly certain: string;
      readonly unlikely: string;
      readonly thresholdsHelp: string;
      readonly fields: Readonly<Record<"candidates" | "topK" | "minRerankScore", Explained>>;
      readonly judgments: Readonly<Record<(typeof JUDGMENTS)[number], Explained>>;
    };
  };
  readonly dashboard: {
    readonly connectionLost: (time: string) => string;
    readonly connectionLostNever: string;
    readonly calm: string;
    readonly pending: (count: number) => string;
    readonly uptime: (duration: Duration) => string;
    readonly consolidated: (days: number) => string;
    readonly neverConsolidated: string;
    readonly cards: {
      readonly todos: string;
      readonly models: string;
      readonly backups: string;
      readonly activity: string;
      readonly index: string;
      readonly service: string;
      readonly clients: string;
    };
    readonly unreadable: string;
    readonly copy: (what: string) => string;
    readonly copied: (what: string) => string;
    readonly modelStates: {
      readonly available: string;
      readonly coolingDown: (clock: string) => string;
      readonly unconfigured: string;
      readonly cooling: string;
    };
    readonly index: {
      readonly lastConsolidation: (date: string) => string;
      readonly never: string;
      readonly daysAgo: (days: number) => string;
      readonly candidates: string;
      readonly formatProblems: string;
      readonly unsummarizedWeeks: string;
    };
    readonly service: {
      readonly uptime: string;
      readonly duration: (duration: Duration) => string;
      readonly version: string;
      readonly mcp: string;
      readonly web: string;
      readonly extraListener: string;
      readonly listening: string;
      readonly listenFailed: string;
      readonly noExtraListener: string;
    };
    readonly backups: {
      readonly none: string;
      readonly kinds: Readonly<Record<"auto" | "manual" | "preRestore", string>>;
      readonly count: (count: number) => string;
      readonly cloud: string;
      readonly cloudNotConfigured: string;
      readonly cloudNever: string;
      readonly cloudSucceeded: string;
      readonly cloudFailed: string;
      readonly backUpNow: string;
      readonly backUpLocal: string;
      readonly backUpCloud: string;
      readonly backedUp: (name: string) => string;
      readonly cloudBackedUp: string;
      readonly failed: string;
      readonly cloudFailedTitle: string;
    };
    readonly probe: {
      readonly button: string;
      readonly note: string;
      readonly succeeded: (milliseconds: number) => string;
      readonly failed: (reason: string, milliseconds: number) => string;
      readonly skipped: string;
      readonly httpStatus: (code: number) => string;
      readonly wrongFormat: string;
      readonly network: string;
      readonly timeout: string;
      readonly failedTitle: string;
    };
    readonly restore: {
      readonly open: string;
      readonly title: string;
      readonly description: string;
      readonly cloudLatest: string;
      readonly empty: string;
      readonly downloading: string;
      readonly reading: string;
      readonly previewFailed: string;
      readonly at: string;
      readonly version: string;
      readonly commit: string;
      readonly files: string;
      readonly replaces: string;
      readonly withConfig: string;
      readonly cancel: string;
      readonly confirm: string;
      readonly failed: string;
      readonly doneTitle: string;
      readonly safety: (name: string) => string;
      readonly reindexed: string;
      readonly done: string;
    };
    readonly reindex: {
      readonly button: string;
      readonly title: string;
      readonly body: string;
      readonly confirm: string;
      readonly cancel: string;
      readonly done: (files: number, units: number) => string;
      readonly failed: string;
    };
    readonly agents: {
      readonly bound: string;
      readonly unbound: string;
      readonly broken: string;
      readonly today: string;
      readonly yesterday: string;
      readonly lastSeen: (date: string) => string;
      readonly neverSeen: string;
      readonly empty: string;
      readonly noBindings: string;
      readonly addBinding: string;
    };
    readonly todos: {
      readonly empty: string;
      readonly modelsUnconfigured: (models: readonly string[]) => string;
      readonly modelsUnconfiguredDetail: (models: readonly string[]) => string;
      readonly noBindings: string;
      readonly noBindingsDetail: string;
      readonly bindingProblem: (client: string) => string;
      readonly extraListenerFailed: (address: string) => string;
      readonly cloudBackupFailed: string;
      readonly cloudBackupFailedDetail: (time: string) => string;
    };
    readonly models: Readonly<Record<string, string>>;
    readonly bindingProblems: Readonly<Record<BindingProblem, string>>;
    readonly activity: {
      readonly window: string;
      readonly weekdays: readonly string[];
      readonly less: string;
      readonly more: string;
      readonly consolidated: string;
      readonly cell: (date: string, total: number, consolidated: boolean) => string;
      readonly activities: (count: number) => string;
      readonly none: string;
      readonly unlabeled: string;
      readonly total: string;
      readonly activeDays: string;
      readonly longestStreak: string;
      readonly topWorkstream: string;
      readonly days: (count: number) => string;
      readonly noWorkstream: string;
      readonly skipped: (files: string) => string;
      readonly historyUnreadable: string;
    };
  };
}

export type MemoryLanguage = components["schemas"]["MemoryLanguage"];

const dictionaries: Record<MemoryLanguage, Dictionary> = { zh, en };

/**
 * The dictionary of the memory language. Before the service has said what that language is, the browser's language decides;
 * the memory language is the interface language, so this is only for the moment before the first answer.
 */
export function dictionaryFor(language: MemoryLanguage | undefined): Dictionary {
  if (language !== undefined) {
    return dictionaries[language];
  }
  return navigator.language.toLowerCase().startsWith("zh") ? zh : en;
}

/**
 * The text to show for a failed call: the service's explanation when it gave one, the unexpected-answer text when the
 * answer had none, and the unreachable text when no answer came at all.
 */
export function errorTextOf(error: unknown, dictionary: Dictionary): string {
  if (error instanceof ApiError) {
    return error.message === "" ? dictionary.unexpectedAnswer : error.message;
  }
  return dictionary.unreachable;
}
