import type { PageListItem } from "../../api/memory";
import type { PageFilter } from "../../lib/routes";

/** The workstream topic pages belong to; listed after every workstream. */
export const TOPICS = "_topics";

/** The sections the service finds by their English names, whatever the memory language, and their labels. */
export type SectionLabels = Readonly<Record<"lesson" | "overturned" | "leftovers", string>>;

/**
 * A section's name as shown: one of the three sections the service recognizes, matched ignoring case as the service
 * does, by its label in the interface language; any other section as written.
 */
export function sectionLabel(name: string, labels: SectionLabels): string {
  const key = name.toLowerCase();
  return key === "lesson" || key === "overturned" || key === "leftovers" ? labels[key] : name;
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
