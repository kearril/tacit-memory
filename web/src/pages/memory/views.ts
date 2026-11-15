/** The five views of the memory page, in the order the sidebar lists them; each is a segment of the address. */
export const MEMORY_VIEWS = ["persona", "workstreams", "pages", "activity", "search"] as const;
export type MemoryView = (typeof MEMORY_VIEWS)[number];

/** Where the memory page lives; `/memory` alone opens the persona. */
export const MEMORY_PATH = "/memory";

/** The address of one view. */
export function memoryPathOf(view: MemoryView): string {
  return `${MEMORY_PATH}/${view}`;
}

/** The anchor of the candidates card on the persona view, which the dashboard links to. */
export const CANDIDATES_ANCHOR = "candidates";
