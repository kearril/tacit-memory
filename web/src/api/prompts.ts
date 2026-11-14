import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "./client";
import type { components } from "./schema";

export type PromptList = components["schemas"]["PromptList"];
export type PromptDocument = components["schemas"]["PromptDocumentView"];
export type PromptName = components["schemas"]["PromptName"];

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
