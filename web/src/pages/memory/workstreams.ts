import { type ActivityReport, calendarColumns, fillWindow, type HeatLevel } from "../dashboard/heatmap";

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
