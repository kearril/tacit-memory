import { cn } from "cn";
import {
  CircleUserRound,
  Compass,
  FolderGit2,
  Globe,
  Heart,
  type LucideIcon,
  ShieldAlert,
  Tag,
  TriangleAlert,
} from "lucide-react";
import { type ReactNode, useId, useState } from "react";
import type { FileProblem, PersonaEntryView, PersonaSection } from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import type { PersonaScope } from "./persona";
import { CodePill, QuoteList, QuoteToggle } from "./parts";
import { splitSources } from "./sources";
import { entryBox } from "../../components/ui/entry-box";

/** The four persona sections. */
export type SectionKind = NonNullable<PersonaSection>;

/** Each section's icon: a red line is told apart by its icon, never by the error colour. */
export const SECTION_ICONS: Readonly<Record<SectionKind, LucideIcon>> = {
  redLines: ShieldAlert,
  workingRules: Compass,
  preferences: Heart,
  profile: CircleUserRound,
};

/** Each scope kind's icon. */
export const SCOPE_ICONS: Readonly<Record<PersonaScope["kind"], LucideIcon>> = {
  global: Globe,
  domain: Tag,
  workstream: FolderGit2,
};

/**
 * One entry in a dashed box. Above its text, a line of what is known about it: what `lead` gives (its scope), the day it
 * was written, a button that opens the user's words when they were kept, and its short code, which shows on hover or
 * focus. A problem with the entry tints the box and is written under the text.
 */
export function PersonaEntryCard({
  entry,
  problems,
  lead,
}: {
  readonly entry: PersonaEntryView;
  readonly problems: readonly FileProblem[];
  readonly lead?: ReactNode;
}) {
  const text = useDictionary().memory.persona;
  const quotesId = useId();
  const [open, setOpen] = useState(false);
  const { lines, sources } = splitSources(entry.continuation);
  const quoted = sources.some((source) => source.quote !== null);
  // The first day the entry was written on leads; any later day it was confirmed on is counted after it and listed on hover.
  const dates = [...new Set(sources.flatMap((source) => (source.date === null ? [] : [source.date])))];
  return (
    <li className={cn(entryBox({ tone: problems.length > 0 ? "warning" : "quiet" }), "group grid gap-xs")}>
      <div className="flex flex-wrap items-center gap-sm text-xs text-ink-muted">
        {lead}
        {dates.length === 0 ? null : (
          <span className="tabular-nums" title={dates.length > 1 ? dates.join(" · ") : undefined}>
            {dates.length > 1 ? `${dates[0]} +${dates.length - 1}` : dates[0]}
          </span>
        )}
        {quoted ? <QuoteToggle open={open} controls={quotesId} onToggle={() => setOpen(!open)} /> : null}
        {entry.code === null ? null : <CodePill code={entry.code} reveal className="ml-auto" />}
      </div>
      <p className={`leading-(--tk-line-height-normal) ${entry.code === null ? "text-ink-muted" : ""}`}>{entry.text}</p>
      {lines.map((line, index) => (
        <p key={index} className="text-sm leading-(--tk-line-height-normal) text-ink-muted">
          {line}
        </p>
      ))}
      {open ? <QuoteList id={quotesId} sources={sources} /> : null}
      {problems.map((problem) => (
        <p key={problem.kind} className="flex items-start gap-xs text-xs text-warning">
          <TriangleAlert aria-hidden className="mt-xs size-(--tk-font-size-xs) shrink-0" />
          {`${problem.kind}: ${problem.description}`}
        </p>
      ))}
      {entry.code === null && problems.length === 0 ? <p className="text-xs text-warning">{text.notEntry}</p> : null}
    </li>
  );
}

/** The small neutral chip a scope or a section is shown in, ahead of an entry. */
const TAG =
  "inline-flex items-center gap-xs rounded-full bg-surface-raised px-sm py-xs leading-(--tk-line-height-tight)";

/** A scope as a small neutral chip with its icon: global, or a domain's or workstream's name. */
export function ScopeChip({ scope }: { readonly scope: PersonaScope }) {
  const text = useDictionary().memory.persona;
  const Icon = SCOPE_ICONS[scope.kind];
  return (
    <span className={TAG}>
      <Icon aria-hidden className="size-(--tk-font-size-xs)" />
      {scope.name ?? text.scopeGlobal}
    </span>
  );
}

/** The persona section an entry is under as a small neutral chip with its icon; one that is not of the four by heading. */
export function SectionChip({
  section,
  heading,
}: {
  readonly section: PersonaSection;
  readonly heading: string | null;
}) {
  const text = useDictionary().memory.persona;
  const Icon = section === null ? TriangleAlert : SECTION_ICONS[section];
  return (
    <span className={cn(TAG, section === null && "text-warning")}>
      <Icon aria-hidden className="size-(--tk-font-size-xs)" />
      {section === null ? text.otherSection(heading) : text.sections[section]}
    </span>
  );
}
