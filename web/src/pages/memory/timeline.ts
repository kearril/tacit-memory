import type { ActivityDayView, ActivityMonthView, ActivitySummaryView } from "../../api/memory";
import { memoryPathOf } from "./views";

/** One entry of a month's timeline: a day with its activities, or a summary between the days. */
export type TimelineEntry =
  | { readonly kind: "day"; readonly date: string; readonly activities: ActivityDayView["activities"] }
  | { readonly kind: "summary"; readonly summary: ActivitySummaryView };

/**
 * A month's timeline: the month summary at the start, then the days newest first, each week summary right before the
 * last day of its week (or, when none of its days is listed, before every day up to its end), so it heads its week.
 * With a workstream, only its activities are kept and the days left empty are dropped; the summaries stay, as they
 * speak for every workstream.
 */
export function timelineOf(view: ActivityMonthView, workstream: string | null): TimelineEntry[] {
  const days = view.days
    .map((day) => ({
      kind: "day" as const,
      date: day.date,
      activities:
        workstream === null ? day.activities : day.activities.filter((item) => item.workstream === workstream),
    }))
    .filter((day) => day.activities.length > 0);
  const months = view.summaries.filter((summary) => summary.kind !== "week");
  const weeks = view.summaries.filter((summary) => summary.kind === "week");

  // Laid out oldest first, each week after its last day, then turned around.
  const oldestFirst: TimelineEntry[] = [];
  let next = 0;
  for (const day of days) {
    while (next < weeks.length && weeks[next]!.last < day.date) {
      oldestFirst.push({ kind: "summary", summary: weeks[next++]! });
    }
    oldestFirst.push(day);
  }
  for (; next < weeks.length; next++) {
    oldestFirst.push({ kind: "summary", summary: weeks[next]! });
  }
  return [...months.map((summary): TimelineEntry => ({ kind: "summary", summary })), ...oldestFirst.reverse()];
}

/** A day of the day strip, with its activity count. */
export interface CalendarDay {
  readonly date: string;
  readonly count: number;
}

/** A `yyyy-MM-dd` date from its parts. */
function dateOf(year: number, month: number, day: number): string {
  return `${String(year).padStart(4, "0")}-${String(month).padStart(2, "0")}-${String(day).padStart(2, "0")}`;
}

/** Every day of a `yyyy-MM` month from the first, each with its count from `counts` (none when missing). */
export function monthDays(month: string, counts: ReadonlyMap<string, number>): CalendarDay[] {
  const [year, number] = month.split("-").map(Number) as [number, number];
  const length = new Date(Date.UTC(year, number, 0)).getUTCDate();
  return Array.from({ length }, (_, index) => {
    const date = dateOf(year, number, index + 1);
    return { date, count: counts.get(date) ?? 0 };
  });
}

/** The `yyyy-MM` month `delta` months after `month`. */
export function shiftMonth(month: string, delta: number): string {
  const [year, number] = month.split("-").map(Number) as [number, number];
  const shifted = new Date(Date.UTC(year, number - 1 + delta, 1));
  return dateOf(shifted.getUTCFullYear(), shifted.getUTCMonth() + 1, 1).slice(0, 7);
}

/** What the activity view shows, as its address holds it. */
export interface ActivityQuery {
  /** The month, `yyyy-MM`. */
  readonly month: string;
  /** The day to go to, `yyyy-MM-dd`; `null` for none. */
  readonly day: string | null;
  /** The workstream to keep; `null` for every one. */
  readonly workstream: string | null;
}

const MONTH = /^\d{4}-(0[1-9]|1[0-2])$/u;
const DAY = /^\d{4}-(0[1-9]|1[0-2])-\d{2}$/u;

/**
 * The activity view's query from its address: `month`, `day` and `workstream`. A month that is not `yyyy-MM` falls back
 * to the month of a valid day, or else to `current`.
 */
export function activityQueryOf(search: URLSearchParams, current: string): ActivityQuery {
  const day = search.get("day");
  const validDay = day !== null && DAY.test(day) ? day : null;
  const month = search.get("month");
  return {
    month: month !== null && MONTH.test(month) ? month : (validDay?.slice(0, 7) ?? current),
    day: validDay,
    workstream: search.get("workstream") || null,
  };
}

/** The address of a query, with only what is set. */
export function activityParams(query: ActivityQuery): URLSearchParams {
  const params = new URLSearchParams({ month: query.month });
  if (query.day !== null) params.set("day", query.day);
  if (query.workstream !== null) params.set("workstream", query.workstream);
  return params;
}

/** The address of the activity view showing a query, for links from the other pages. */
export function activityPathOf(query: ActivityQuery): string {
  return `${memoryPathOf("activity")}?${activityParams(query)}`;
}

/** The anchor of a day on the timeline. */
export function dayAnchor(date: string): string {
  return `day-${date}`;
}
