import type { PageListItem, PageType } from "../../api/memory";

/** The page types, in the order the filter lists them. */
export const PAGE_TYPES = ["incident", "decision", "entity", "work"] as const satisfies readonly PageType[];

/** The workstream topic pages belong to; listed after every workstream. */
export const TOPICS = "_topics";

/** The list's filter, as the address holds it; an empty value filters nothing. */
export interface PageFilter {
  readonly workstream: string;
  readonly type: PageType | "";
  readonly q: string;
}

/** The filter the address holds; a type that is not one of the four is left out. */
export function pageFilterOf(params: URLSearchParams): PageFilter {
  const type = params.get("type") ?? "";
  return {
    workstream: params.get("workstream") ?? "",
    type: (PAGE_TYPES as readonly string[]).includes(type) ? (type as PageType) : "",
    q: params.get("q") ?? "",
  };
}

/** The address of a filter: only what is set. */
export function pageFilterParams(filter: PageFilter): URLSearchParams {
  const params = new URLSearchParams();
  for (const key of ["workstream", "type", "q"] as const) {
    if (filter[key] !== "") {
      params.set(key, filter[key]);
    }
  }
  return params;
}

/** The pages the filter keeps: of its workstream and type, with the keyword in the title or an alias, ignoring case. */
export function filterPages(pages: readonly PageListItem[], filter: PageFilter): readonly PageListItem[] {
  const q = filter.q.trim().toLowerCase();
  return pages.filter(
    (page) =>
      (filter.workstream === "" || page.workstream === filter.workstream) &&
      (filter.type === "" || page.type === filter.type) &&
      (q === "" || [page.title, ...page.aliases].some((name) => name.toLowerCase().includes(q))),
  );
}

/** How the list is ordered: by workstream as it groups them, by the day changed, or by uses. */
export type PageSort = "workstream" | "changed" | "uses";

/**
 * The pages in the list's order. By workstream, in name order with the topics last; by the day changed, newest first
 * with the undated last; by uses, most first. Pages that tie keep their order.
 */
export function sortPages(pages: readonly PageListItem[], sort: PageSort): PageListItem[] {
  const rows = [...pages];
  if (sort === "changed") {
    return rows.sort((a, b) => (b.changed ?? "").localeCompare(a.changed ?? ""));
  }
  if (sort === "uses") {
    return rows.sort((a, b) => b.uses - a.uses);
  }
  return rows.sort((a, b) =>
    a.workstream === b.workstream
      ? 0
      : a.workstream === TOPICS
        ? 1
        : b.workstream === TOPICS
          ? -1
          : a.workstream.localeCompare(b.workstream),
  );
}

/** The anchor of a page's section, which the table of contents and the search results lead to. */
export function sectionAnchor(name: string): string {
  return `section-${name}`;
}
