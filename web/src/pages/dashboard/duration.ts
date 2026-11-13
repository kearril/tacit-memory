/** A length of time in whole days, hours and minutes, each part below the next unit. */
export interface Duration {
  readonly days: number;
  readonly hours: number;
  readonly minutes: number;
}

/** Splits a number of seconds into whole days, hours and minutes; the seconds left over are dropped. */
export function durationOf(seconds: number): Duration {
  const minutes = Math.floor(Math.max(0, seconds) / 60);
  return { days: Math.floor(minutes / 1440), hours: Math.floor((minutes % 1440) / 60), minutes: minutes % 60 };
}

/**
 * The seconds of a cooldown still left now: the count the report gave, less the whole seconds since the report was
 * fetched, never below zero.
 */
export function remainingOf(seconds: number, fetchedAt: number, now: number): number {
  return Math.max(0, seconds - Math.floor((now - fetchedAt) / 1000));
}

/** A count of seconds as a clock: minutes and seconds ("4:05"), with hours in front from an hour up ("1:02:03"). */
export function clockOf(seconds: number): string {
  const whole = Math.max(0, Math.floor(seconds));
  const hours = Math.floor(whole / 3600);
  const minutes = Math.floor((whole % 3600) / 60);
  const rest = String(whole % 60).padStart(2, "0");
  return hours > 0 ? `${hours}:${String(minutes).padStart(2, "0")}:${rest}` : `${minutes}:${rest}`;
}
