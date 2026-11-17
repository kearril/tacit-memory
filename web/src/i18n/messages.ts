import { ApiError } from "../api/client";
import type { components } from "../api/schema";
import { en } from "./en";
import { zh } from "./zh";

/** A setting's name and the sentence or two that explains it. */
export interface Explained {
  readonly label: string;
  readonly help: string;
}

/** The text of the interface; every language has the shape of the Chinese one. */
export type Dictionary = typeof zh;

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
