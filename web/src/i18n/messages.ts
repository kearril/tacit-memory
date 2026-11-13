import type { components } from "../api/schema";
import { en } from "./en";
import { zh } from "./zh";

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
  readonly loading: string;
  readonly unreachable: string;
  readonly menuOpen: string;
  readonly menuClose: string;
  readonly logout: string;
  readonly showPassword: string;
  readonly hidePassword: string;
  readonly login: {
    readonly title: string;
    readonly description: string;
    readonly passwordLabel: string;
    readonly submit: string;
    readonly submitting: string;
  };
  readonly access: {
    readonly title: string;
    readonly passwordSet: string;
    readonly passwordUnset: string;
    readonly note: string;
    readonly newPassword: string;
    readonly currentPassword: string;
    readonly setPassword: string;
    readonly changePassword: string;
    readonly saving: string;
    readonly saved: string;
    readonly clearPassword: string;
    readonly clearTitle: string;
    readonly clearBody: string;
    readonly clearConfirm: string;
    readonly cancel: string;
    readonly cleared: string;
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
