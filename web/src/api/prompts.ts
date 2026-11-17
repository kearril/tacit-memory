import { keepPreviousData, useQuery } from "@tanstack/react-query";
import { api, call } from "./client";
import type { Affects } from "./effects";
import { STATUS_QUERY_KEY } from "./status";
import type { components } from "./schema";

export type PromptList = components["schemas"]["PromptList"];
export type PromptDocument = components["schemas"]["PromptDocumentView"];
export type PromptName = components["schemas"]["PromptName"];
export type PromptSaved = components["schemas"]["PromptSaved"];
export type PromptMeasure = components["schemas"]["PromptMeasure"];
export type PromptCheck = components["schemas"]["PromptCheck"];
export type ResidentPart = components["schemas"]["ResidentPart"];

/** The query key of the prompt documents. */
export const PROMPTS_QUERY_KEY = ["prompts"] as const;

/**
 * The four prompt documents: each file as it is now with its hash, or the built-in text where there is no file. Fetched
 * once per visit of the prompts page; not on window focus, as a fetch while the user edits must not move the text under them.
 */
export function usePrompts() {
  return useQuery({
    queryKey: PROMPTS_QUERY_KEY,
    queryFn: () => call(api.GET("/api/prompts")),
    refetchOnWindowFocus: false,
  });
}

/**
 * Saves `text` as the document `name`, based on the file whose hash is `hash` (`null` when there was no file): the service
 * checks it, writes it and applies it at once. A file changed since is refused (`PromptChanged`); a text that breaks the
 * document's rules is refused with every problem (`PromptRejected`). The answer holds the document as saved and what did
 * not go as planned, such as a resident block that could not be rendered again.
 */
export function savePrompt(name: PromptName, hash: string | null, text: string): Promise<PromptSaved> {
  return call(api.PUT("/api/prompts/{name}", { params: { path: { name } }, body: { hash, text } }));
}

/** Returns the document `name` to its built-in text by deleting its file, applied at once as a save is. */
export function restorePromptDefault(name: PromptName): Promise<PromptSaved> {
  return call(api.DELETE("/api/prompts/{name}", { params: { path: { name } } }));
}

/**
 * A save or a return to the default changes the documents (the core rules and the recording criteria are measured
 * together, so the other one's length as well) and may fix a broken file, which the status lists.
 */
export const SAVE_PROMPT_AFFECTS: Affects = [PROMPTS_QUERY_KEY, STATUS_QUERY_KEY];

/** Checks `text` as the document `name` without saving it: its measure, and every refusal a save of it would give. */
export function checkPrompt(name: PromptName, text: string): Promise<PromptCheck> {
  return call(api.POST("/api/prompts/{name}/check", { params: { path: { name } }, body: { text } }));
}

/**
 * The check of `text` as the document `name`, while `enabled`. The last answer stays shown while the next is on its way,
 * so the measure does not flicker as the user types. A failed check is not tried again: the editor goes on without it.
 */
export function usePromptCheck(name: PromptName, text: string, enabled: boolean) {
  return useQuery({
    queryKey: [...PROMPTS_QUERY_KEY, name, "check", text],
    queryFn: () => checkPrompt(name, text),
    enabled,
    retry: false,
    refetchOnWindowFocus: false,
    placeholderData: keepPreviousData,
  });
}
