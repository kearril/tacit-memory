import type { ActivityReport } from "../../api/activity";
import type { components } from "../../api/schema";

export type { ActivityReport };
export type WorkstreamCount = components["schemas"]["WorkstreamCount"];

/** One day of the window, with or without activities. */
export interface HeatDay {
  /** The day as `yyyy-MM-dd`. */
  readonly date: string;
  readonly total: number;
  /** The count per workstream label, most first, as the service gives it. */
  readonly workstreams: readonly WorkstreamCount[];
  readonly unlabeled: number;
  /** Whether a consolidation happened that day. */
  readonly consolidated: boolean;
}

/** A shade of the heatmap: 0 is an empty cell, 1 to 4 ever more activities. */
export type HeatLevel = 0 | 1 | 2 | 3 | 4;

const MS_PER_DAY = 86_400_000;

/** The day count since the epoch of a `yyyy-MM-dd` date, read as a calendar date with no time zone. */
function dayNumber(date: string): number {
  const [year, month, day] = date.split("-").map(Number);
  return Date.UTC(year, month - 1, day) / MS_PER_DAY;
}

/** The `yyyy-MM-dd` date of a day count since the epoch. */
function dateOf(number: number): string {
  return new Date(number * MS_PER_DAY).toISOString().slice(0, 10);
}

/**
 * Every day of the window, from its first day to today, in order. The service lists only the days with activities; the
 * others are filled in as empty days.
 */
export function fillWindow(report: ActivityReport): HeatDay[] {
  const listed = new Map(report.days.map((day) => [day.day, day]));
  const consolidated = new Set(report.consolidationDays);
  const days: HeatDay[] = [];
  for (let number = dayNumber(report.from); number <= dayNumber(report.to); number++) {
    const date = dateOf(number);
    const day = listed.get(date);
    const workstreams = day?.workstreams ?? [];
    const unlabeled = day?.unlabeled ?? 0;
    days.push({
      date,
      total: workstreams.reduce((sum, item) => sum + item.count, unlabeled),
      workstreams,
      unlabeled,
      consolidated: consolidated.has(date),
    });
  }
  return days;
}

/**
 * The calendar's columns: one per week, Monday to Sunday from top to bottom. The window starts on a Monday, so each column
 * is a whole week; the days after today in the last column are `null`, empty places with no cell.
 */
export function calendarColumns(days: readonly HeatDay[]): (HeatDay | null)[][] {
  const columns: (HeatDay | null)[][] = [];
  for (let start = 0; start < days.length; start += 7) {
    const week: (HeatDay | null)[] = days.slice(start, start + 7);
    while (week.length < 7) {
      week.push(null);
    }
    columns.push(week);
  }
  return columns;
}

/**
 * The months to label above the calendar: the first column whose Monday falls in a new month carries that month's label.
 * The first column is labelled only when its Monday is within the first week of its month, so a label never sits over
 * a month that has barely started.
 */
export function monthLabels(columns: readonly (readonly (HeatDay | null)[])[]): { column: number; date: string }[] {
  const labels: { column: number; date: string }[] = [];
  columns.forEach((week, column) => {
    const monday = week[0];
    if (monday === null) {
      return;
    }
    const month = monday.date.slice(0, 7);
    const previous = column === 0 ? null : columns[column - 1][0]?.date.slice(0, 7);
    if (column === 0 ? Number(monday.date.slice(8)) <= 7 : month !== previous) {
      labels.push({ column, date: monday.date });
    }
  });
  return labels;
}

/** The shade of a day: none; up to 2; up to 4; up to 7; 8 or more. */
export function dayLevel(total: number): HeatLevel {
  if (total <= 0) return 0;
  if (total <= 2) return 1;
  if (total <= 4) return 2;
  if (total <= 7) return 3;
  return 4;
}

/** The four numbers under the heatmap. */
export interface HeatStats {
  readonly total: number;
  readonly activeDays: number;
  /** The most days in a row with activities, within the window. */
  readonly longestStreak: number;
  /** The workstream with the most activities in the window, ties going to the name first in order; `null` when none has any. */
  readonly topWorkstream: WorkstreamCount | null;
}

export function statsOf(days: readonly HeatDay[]): HeatStats {
  let total = 0;
  let activeDays = 0;
  let streak = 0;
  let longestStreak = 0;
  const byWorkstream = new Map<string, number>();
  for (const day of days) {
    total += day.total;
    if (day.total > 0) {
      activeDays++;
      streak++;
      longestStreak = Math.max(longestStreak, streak);
    } else {
      streak = 0;
    }
    for (const item of day.workstreams) {
      byWorkstream.set(item.workstream, (byWorkstream.get(item.workstream) ?? 0) + item.count);
    }
  }

  let topWorkstream: WorkstreamCount | null = null;
  for (const [workstream, count] of [...byWorkstream].sort(([a], [b]) => (a < b ? -1 : a > b ? 1 : 0))) {
    if (topWorkstream === null || count > topWorkstream.count) {
      topWorkstream = { workstream, count };
    }
  }
  return { total, activeDays, longestStreak, topWorkstream };
}
