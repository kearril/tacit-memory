import type { PageListItem, RecognizedSection } from "../../api/memory";
import type { PageFilter } from "../../lib/routes";

/** The workstream topic pages belong to; listed after every workstream. */
export const TOPICS = "_topics";

/**
 * A section's name as shown: a section the service recognizes by its label in the interface language, any other as
 * written.
 */
export function sectionLabel(
  section: { readonly name: string; readonly recognized: RecognizedSection | null },
  labels: Readonly<Record<RecognizedSection, string>>,
): string {
  return section.recognized === null ? section.name : labels[section.recognized];
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
  if (sort === "changed") {
    return pages.toSorted((a, b) => (b.changed ?? "").localeCompare(a.changed ?? ""));
  }
  if (sort === "uses") {
    return pages.toSorted((a, b) => b.uses - a.uses);
  }
  return pages.toSorted((a, b) =>
    a.workstream === b.workstream
      ? 0
      : a.workstream === TOPICS
        ? 1
        : b.workstream === TOPICS
          ? -1
          : a.workstream.localeCompare(b.workstream),
  );
}
