import type { ServiceLogDay, ServiceLogEntry } from "../../api/logs";
import { applyFilters } from "./filter";
import { type LogFilters } from "../../lib/routes";

/** How often the page asks for new events while it follows today's log, in milliseconds. */
export const FOLLOW_INTERVAL = 2000;

/** How close to the end of the page, in pixels, still counts as reading the newest event. */
const BOTTOM_TOLERANCE = 48;

/** The events a followed day holds so far, and how many the file had at the last read: the `after` of the next. */
export interface HeldLog {
  readonly entries: readonly ServiceLogEntry[];
  readonly next: number;
}

/** The events held with a page of new ones after them; the same object when the page brings nothing new. */
export function appendPage(held: HeldLog, page: ServiceLogDay): HeldLog {
  if (page.entries.length === 0 && page.next === held.next) {
    return held;
  }
  return { entries: [...held.entries, ...page.entries], next: page.next };
}

/** The new day when the service's today has moved past the day followed: the page goes on to it. */
export function dayTurned(page: ServiceLogDay): string | undefined {
  return page.today > page.date ? page.today : undefined;
}

/** Whether the view shows the end of the page, so new events should scroll into it. */
export function isAtBottom(view: {
  readonly scrollTop: number;
  readonly clientHeight: number;
  readonly scrollHeight: number;
}): boolean {
  return view.scrollHeight - view.scrollTop - view.clientHeight <= BOTTOM_TOLERANCE;
}

/** How many of the new events the reader would see under the filters: the count the "new events" button shows. */
export function unseenOf(fresh: readonly ServiceLogEntry[], filters: LogFilters): number {
  return applyFilters(fresh, filters).length;
}
