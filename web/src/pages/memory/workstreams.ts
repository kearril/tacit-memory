import type { BriefPage, BriefPageGroup, PageType } from "../../api/memory";
import { type ActivityReport, calendarColumns, fillWindow, type HeatLevel } from "../../lib/heatmap";

/** How many weeks a workstream's mini activity bar shows: about half a year. */
export const MINI_WEEKS = 26;

/** One week of a workstream's mini activity bar. */
export interface WorkstreamWeek {
  /** The week's Monday as `yyyy-MM-dd`. */
  readonly monday: string;
  readonly count: number;
}

/**
 * One workstream's activities per week over the last {@link MINI_WEEKS} weeks, oldest first and the current week last,
 * from the same counts the dashboard heatmap reads. A window shorter than that gives only its own weeks.
 */
export function workstreamWeeks(report: ActivityReport, workstream: string): WorkstreamWeek[] {
  return calendarColumns(fillWindow(report))
    .slice(-MINI_WEEKS)
    .map((week) => ({
      monday: week[0]!.date,
      count: week.reduce(
        (sum, day) => sum + (day?.workstreams.find((item) => item.workstream === workstream)?.count ?? 0),
        0,
      ),
    }));
}

/** The shade of a week, on the heatmap's colours: none; up to 3; up to 7; up to 14; 15 or more. */
export function weekLevel(count: number): HeatLevel {
  if (count <= 0) return 0;
  if (count <= 3) return 1;
  if (count <= 7) return 2;
  if (count <= 14) return 3;
  return 4;
}

/** How the page directory of a brief is ordered: as the brief groups it by type, by the day changed, or by uses. */
export type DirectorySort = "type" | "changed" | "uses";

/** A page of the directory with the type of the group it is in. */
export interface DirectoryRow {
  readonly type: PageType | null;
  readonly page: BriefPage;
}

/**
 * The brief's pages in one list: by type as the brief groups them, or across the groups by the day changed (newest
 * first, pages without one last) or by uses (most first). A tie keeps the brief's order.
 */
export function directoryRows(groups: readonly BriefPageGroup[], sort: DirectorySort): DirectoryRow[] {
  const rows = groups.flatMap((group) => group.pages.map((page) => ({ type: group.type, page })));
  if (sort === "changed") {
    return [...rows].sort((a, b) => (b.page.changed ?? "").localeCompare(a.page.changed ?? ""));
  }
  if (sort === "uses") {
    return [...rows].sort((a, b) => b.page.uses - a.page.uses);
  }
  return rows;
}

/** A list marker at the start of an open item's line: `-`, `*`, `+` or a number with a dot, and the space after it. */
const LIST_MARKER = /^\s*(?:[-*+]|\d+\.)\s+/u;

/** An open item's line as it reads, without the list marker it is written with in the page. */
export function openItemText(line: string): string {
  return line.replace(LIST_MARKER, "");
}
