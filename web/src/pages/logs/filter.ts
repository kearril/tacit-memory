import type { ServiceLogEntry, ServiceLogLevel } from "../../api/logs";
import type { LogFilters } from "../../lib/routes";

/** The events at a level switched on whose message holds the keyword, in any case; a keyword of spaces only is none. */
export function applyFilters(entries: readonly ServiceLogEntry[], filters: LogFilters): readonly ServiceLogEntry[] {
  const query = filters.query.trim().toLocaleLowerCase();
  return entries.filter(
    (entry) => filters.levels.has(entry.level) && (query === "" || entry.message.toLocaleLowerCase().includes(query)),
  );
}

/** How many events of each level the day holds, before any filter. */
export function levelCounts(entries: readonly ServiceLogEntry[]): Readonly<Record<ServiceLogLevel, number>> {
  const counts = { information: 0, warning: 0, error: 0 };
  for (const entry of entries) {
    counts[entry.level] += 1;
  }
  return counts;
}

/** One piece of a message: text as written, and whether it is a match of the keyword. */
export interface HighlightPart {
  readonly text: string;
  readonly hit: boolean;
}

/** The message cut into the matches of the keyword, in any case, and the text between them. */
export function highlightParts(text: string, query: string): readonly HighlightPart[] {
  const needle = query.trim().toLocaleLowerCase();
  if (needle === "") {
    return [{ text, hit: false }];
  }
  const haystack = text.toLocaleLowerCase();
  const parts: HighlightPart[] = [];
  let from = 0;
  for (let at = haystack.indexOf(needle); at !== -1; at = haystack.indexOf(needle, from)) {
    if (at > from) {
      parts.push({ text: text.slice(from, at), hit: false });
    }
    parts.push({ text: text.slice(at, at + needle.length), hit: true });
    from = at + needle.length;
  }
  if (from < text.length) {
    parts.push({ text: text.slice(from), hit: false });
  }
  return parts;
}
