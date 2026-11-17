import { scopeParamOf } from "../../lib/routes";
import type {
  CandidateOccurrenceView,
  PersonaEntryView,
  PersonaReport,
  PersonaSection,
  PersonaSectionView,
} from "../../api/memory";

/** The four persona sections, in the order the persona file and the view give them. */
export const SECTION_ORDER = ["redLines", "workingRules", "preferences", "profile"] as const;

/** Where a persona entry is read: every session, a domain when its trigger matches, or one workstream. */
export interface PersonaScope {
  /** `global`, `domain:<label>` or `workstream:<name>`; what the address names. */
  readonly key: string;
  readonly kind: "global" | "domain" | "workstream";
  /** The domain's label or the workstream's name; `null` for global. */
  readonly name: string | null;
  /** A domain's trigger; `null` when it is missing and for the other kinds. */
  readonly trigger: string | null;
  /** How many entries the scope holds, not counting lines that are not entries. */
  readonly count: number;
}

/** One entry with the scope it is in and, under a section that is not one of the four, that section's heading. */
export interface PersonaRow {
  readonly scope: PersonaScope;
  readonly heading: string | null;
  readonly entry: PersonaEntryView;
}

/** A section's rows across the scopes; `section` is `null` for what sits under no known section. */
export interface PersonaSectionRows {
  readonly section: PersonaSection | null;
  readonly rows: readonly PersonaRow[];
}

function countOf(sections: readonly PersonaSectionView[]): number {
  return sections.flatMap((section) => section.entries).filter((entry) => entry.code !== null).length;
}

/** The persona's scopes in the file's order: global, then each domain, then each workstream. */
export function personaScopes(report: PersonaReport): readonly PersonaScope[] {
  return [
    { key: "global", kind: "global", name: null, trigger: null, count: countOf(report.global) },
    ...report.domains.map((block) => ({
      key: `domain:${block.name}`,
      kind: "domain" as const,
      name: block.name,
      trigger: block.trigger,
      count: countOf(block.sections),
    })),
    ...report.workstreams.map((block) => ({
      key: `workstream:${block.name}`,
      kind: "workstream" as const,
      name: block.name,
      trigger: null,
      count: countOf(block.sections),
    })),
  ];
}

/**
 * The persona by section: the four sections in order, each with every scope's entries in the file's order, then what
 * sits under no known section when there is any. With a scope key, only that scope's entries.
 */
export function personaSections(report: PersonaReport, scopeKey: string | null): readonly PersonaSectionRows[] {
  const scopes = personaScopes(report);
  const blocks = [
    report.global,
    ...report.domains.map((b) => b.sections),
    ...report.workstreams.map((b) => b.sections),
  ];
  const rows = blocks.flatMap((sections, index) => {
    const scope = scopes[index]!;
    if (scopeKey !== null && scope.key !== scopeKey) {
      return [];
    }
    return sections.flatMap((section) =>
      section.entries.map((entry) => ({ section: section.section, row: { scope, heading: section.heading, entry } })),
    );
  });
  const known = SECTION_ORDER.map((section) => ({
    section,
    rows: rows.filter((row) => row.section === section).map((row) => row.row),
  }));
  const other = rows.filter((row) => row.section === null).map((row) => row.row);
  return other.length === 0 ? known : [...known, { section: null, rows: other }];
}

/** The scope the address names, when the persona has it; otherwise `null`, the whole persona. */
export function scopeKeyOf(params: URLSearchParams, report: PersonaReport): string | null {
  const key = scopeParamOf(params);
  return personaScopes(report).some((scope) => scope.key === key) ? key : null;
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
