import type { ServiceLogDays } from "../../api/logs";

/** The days to choose from, newest first: today, which may have no file yet, and every day with one. */
export function dateOptions(days: Pick<ServiceLogDays, "today" | "dates">): readonly string[] {
  return [days.today, ...days.dates.filter((date) => date !== days.today)];
}

/**
 * The time of day an event was written, to the millisecond, as the service's clock showed it: read from the text, so a
 * browser in another time zone shows the same time as the file.
 */
export function clockOf(time: string | null): string | undefined {
  const match = time === null ? null : /T(\d{2}:\d{2}:\d{2})(?:\.(\d+))?/.exec(time);
  if (match === null) {
    return undefined;
  }
  return `${match[1]}.${(match[2] ?? "").padEnd(3, "0").slice(0, 3)}`;
}
