import type { CandidateOccurrenceView } from "../../api/memory";

/** Where an entry came from: the day it was written, and the user's words when they were kept. */
export interface Source {
  readonly date: string | null;
  readonly quote: string | null;
}

/** A comment line: `<!-- … -->`, with any spaces around it. */
const COMMENT = /^\s*<!--\s*(.*?)\s*-->\s*$/u;

/** A day at the start of a comment. */
const DAY = /^(\d{4}-\d{2}-\d{2})(?:\s+|$)/u;

/** The quote mark in either memory language, then the words up to the last closing bracket. */
const QUOTE = /^(?:原话|quote)「(.*)」$/iu;

/**
 * The source a comment line records: `<!-- 2026-10-25 原话「…」 -->`, with the day or the quote left out when the line
 * has none. `null` for a line that is not a comment, or a comment that names neither.
 */
export function sourceOf(line: string): Source | null {
  const comment = COMMENT.exec(line);
  if (comment === null) {
    return null;
  }
  let rest = comment[1] ?? "";
  const day = DAY.exec(rest);
  const date = day === null ? null : (day[1] ?? null);
  rest = day === null ? rest : rest.slice(day[0].length);
  const quote = QUOTE.exec(rest.trim());
  if (quote === null && (date === null || rest.trim() !== "")) {
    return null;
  }
  return { date, quote: quote === null ? null : (quote[1] ?? "") };
}

/**
 * An entry's continuation lines parted into the lines of its text and its sources, each in order. A comment that is not
 * a source is left out, as the persona's entry text leaves every comment out.
 */
export function splitSources(continuation: readonly string[]): {
  readonly lines: readonly string[];
  readonly sources: readonly Source[];
} {
  const lines: string[] = [];
  const sources: Source[] = [];
  for (const line of continuation) {
    const source = sourceOf(line);
    if (source !== null) {
      sources.push(source);
    } else if (COMMENT.exec(line) === null) {
      lines.push(line);
    }
  }
  return { lines, sources };
}

/** A day a candidate was said on, once however many records it has; rejected when any of them is. */
export interface OccurrenceDay {
  readonly date: string;
  readonly rejected: boolean;
}

/** The days of a candidate's occurrence records, each once, in the order they first appear. */
export function occurrenceDaysOf(occurrences: readonly CandidateOccurrenceView[]): readonly OccurrenceDay[] {
  const days = new Map<string, boolean>();
  for (const occurrence of occurrences) {
    days.set(occurrence.date, (days.get(occurrence.date) ?? false) || occurrence.rejected);
  }
  return [...days].map(([date, rejected]) => ({ date, rejected }));
}
