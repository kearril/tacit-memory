import type { components } from "../api/schema";
import { en } from "./en";
import { zh } from "./zh";

/**
 * The text of the interface, in both memory languages. Every dictionary is annotated with this type, so a dictionary that
 * leaves a key out or adds one does not compile.
 */
export interface Dictionary {
  readonly connected: string;
  readonly serviceVersion: string;
  readonly unreachable: string;
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
