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

/** A size in bytes in the largest unit that keeps it at one or more, with at most one decimal: "512 B", "3.4 MB". */
export function sizeOf(bytes: number, locale: string): string {
  const units = ["byte", "kilobyte", "megabyte", "gigabyte"] as const;
  let value = Math.max(0, bytes);
  let unit = 0;
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024;
    unit += 1;
  }
  return new Intl.NumberFormat(locale, {
    style: "unit",
    unit: units[unit],
    unitDisplay: "short",
    maximumFractionDigits: unit === 0 ? 0 : 1,
  }).format(value);
}

/** A calendar day, `yyyy-MM-dd`, as the interface shows it: that day whatever the browser's time zone, in the given parts. */
export function dayText(day: string, locale: string, parts: Intl.DateTimeFormatOptions): string {
  return new Date(`${day}T00:00:00Z`).toLocaleDateString(locale, { ...parts, timeZone: "UTC" });
}

/** A moment as a medium date and a short time, in the browser's time zone. */
export function timeText(at: string, locale: string): string {
  return new Date(at).toLocaleString(locale, { dateStyle: "medium", timeStyle: "short" });
}

/** The file name at the end of a path, whichever separator the host uses. */
export function fileNameOf(path: string): string {
  return path.split(/[\\/]/).pop() || path;
}

/** A version without its build metadata: "1.0.0+cd36d27" shows as "1.0.0". */
export function versionText(version: string): string {
  return version.split("+")[0];
}
