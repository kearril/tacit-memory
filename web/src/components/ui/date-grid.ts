/** One day a millisecond count apart, for stepping dates in UTC, where no day is longer or shorter than this. */
const MS_PER_DAY = 86_400_000;

/** The UTC midnight of a `yyyy-MM-dd` date. */
function timeOf(date: string): number {
  const [year, month, day] = date.split("-").map(Number) as [number, number, number];
  return Date.UTC(year, month - 1, day);
}

/** The `yyyy-MM-dd` date of a UTC time. */
function dateOfTime(time: number): string {
  return new Date(time).toISOString().slice(0, 10);
}

/** Whether `value` is a real `yyyy-MM-dd` calendar date, not merely one in that shape such as `2026-02-30`. */
export function isDate(value: string): boolean {
  return /^\d{4}-\d{2}-\d{2}$/.test(value) && dateOfTime(timeOf(value)) === value;
}

/** The date `days` days after `date` (before it when negative). */
export function addDays(date: string, days: number): string {
  return dateOfTime(timeOf(date) + days * MS_PER_DAY);
}

/**
 * The same day `months` months after `date`, kept within the shorter month: a month after the 31st of January is the
 * last of February.
 */
export function addMonths(date: string, months: number): string {
  const [year, month, day] = date.split("-").map(Number) as [number, number, number];
  const first = new Date(Date.UTC(year, month - 1 + months, 1));
  const length = new Date(Date.UTC(first.getUTCFullYear(), first.getUTCMonth() + 1, 0)).getUTCDate();
  return dateOfTime(Date.UTC(first.getUTCFullYear(), first.getUTCMonth(), Math.min(day, length)));
}

/** The `yyyy-MM` month of a date. */
export function monthOf(date: string): string {
  return date.slice(0, 7);
}

/**
 * The calendar of the month `date` falls in: whole weeks from Monday to Sunday, from the week of its first day to the
 * week of its last, so the first and last weeks carry days of the months around it.
 */
export function monthWeeks(date: string): string[][] {
  const first = `${monthOf(date)}-01`;
  const start = addDays(first, -((new Date(timeOf(first)).getUTCDay() + 6) % 7));
  const last = addDays(addMonths(first, 1), -1);
  const weeks: string[][] = [];
  for (let monday = start; monday <= last; monday = addDays(monday, 7)) {
    weeks.push(Array.from({ length: 7 }, (_, index) => addDays(monday, index)));
  }
  return weeks;
}

/** Today's `yyyy-MM-dd` date where the browser is. */
export function today(): string {
  const now = new Date();
  return dateOfTime(Date.UTC(now.getFullYear(), now.getMonth(), now.getDate()));
}
