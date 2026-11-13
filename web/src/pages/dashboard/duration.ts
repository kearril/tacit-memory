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
