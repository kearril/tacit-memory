import type { PageListItem } from "../../api/memory";

/** The four page types. */
export const PAGE_TYPES = ["incident", "decision", "entity", "work"] as const;

export type PageType = (typeof PAGE_TYPES)[number];

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

/** The pages by workstream, in name order with the topics last; each group keeps the pages' order. */
export function groupPages(
  pages: readonly PageListItem[],
): readonly { readonly workstream: string; readonly pages: readonly PageListItem[] }[] {
  const groups = new Map<string, PageListItem[]>();
  for (const page of pages) {
    groups.set(page.workstream, [...(groups.get(page.workstream) ?? []), page]);
  }
  return [...groups]
    .sort(([a], [b]) => (a === TOPICS ? 1 : b === TOPICS ? -1 : a.localeCompare(b)))
    .map(([workstream, grouped]) => ({ workstream, pages: grouped }));
}

/** The anchor of a page's section, which the table of contents and the search results lead to. */
export function sectionAnchor(name: string): string {
  return `section-${name}`;
}
