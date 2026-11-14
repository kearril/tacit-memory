import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "./client";
import type { components } from "./schema";

export type PromptList = components["schemas"]["PromptList"];
export type PromptDocument = components["schemas"]["PromptDocumentView"];
export type PromptName = components["schemas"]["PromptName"];
export type PromptSaved = components["schemas"]["PromptSaved"];

/** The query key of the prompt documents. */
export const PROMPTS_QUERY_KEY = ["prompts"] as const;

/**
 * The four prompt documents: each file as it is now with its hash, or the built-in text where there is no file. Fetched
 * once per visit of the prompts page; not on window focus, as a fetch while the user edits must not move the text under them.
 */
export function usePrompts() {
  return useQuery({
    queryKey: PROMPTS_QUERY_KEY,
    queryFn: () => apiFetch<PromptList>("/api/prompts"),
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
  return apiFetch<PromptSaved>(`/api/prompts/${name}`, { method: "PUT", body: JSON.stringify({ hash, text }) });
}

/** Returns the document `name` to its built-in text by deleting its file, applied at once as a save is. */
export function restorePromptDefault(name: PromptName): Promise<PromptSaved> {
  return apiFetch<PromptSaved>(`/api/prompts/${name}`, { method: "DELETE" });
}
