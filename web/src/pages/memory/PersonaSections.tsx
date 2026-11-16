import {
  ChevronDown,
  CircleUserRound,
  Compass,
  FolderGit2,
  Globe,
  Heart,
  type LucideIcon,
  MessageSquareQuote,
  ShieldAlert,
  Tag,
  TriangleAlert,
} from "lucide-react";
import { type ReactNode, useId, useState } from "react";
import type { FileProblem, PersonaEntryView, PersonaSection } from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import type { PersonaScope } from "./persona";
import { CodePill } from "./parts";
import { splitSources } from "./sources";

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
  const quoted = sources.filter((source) => source.quote !== null);
  const date = sources.find((source) => source.date !== null)?.date ?? null;
  return (
    <li
      className={`group grid gap-xs rounded-md border-(length:--tk-border-thin) border-dashed px-md py-sm ${
        problems.length > 0 ? "border-warning bg-warning-soft" : "border-ink-muted/40"
      }`}
    >
      <div className="flex flex-wrap items-center gap-sm text-xs text-ink-muted">
        {lead}
        {date === null ? null : <span className="tabular-nums">{date}</span>}
        {quoted.length === 0 ? null : (
          <button
            type="button"
            aria-expanded={open}
            aria-controls={quotesId}
            onClick={() => setOpen(!open)}
            className={`inline-flex cursor-pointer items-center gap-xs rounded-full px-sm py-xs leading-(--tk-line-height-tight) font-medium transition-colors outline-none hover:bg-accent hover:text-on-accent focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
              open ? "bg-accent text-on-accent" : "bg-accent-soft text-accent"
            }`}
          >
            <MessageSquareQuote aria-hidden className="size-(--tk-font-size-sm)" />
            {text.quoteToggle}
            <ChevronDown
              aria-hidden
              className={`size-(--tk-font-size-sm) transition-transform ${open ? "rotate-180" : ""}`}
            />
          </button>
        )}
        {entry.code === null ? null : (
          <CodePill
            code={entry.code}
            className="ml-auto opacity-0 transition-opacity group-focus-within:opacity-100 group-hover:opacity-100"
          />
        )}
      </div>
      <p className={`leading-(--tk-line-height-normal) ${entry.code === null ? "text-ink-muted" : ""}`}>{entry.text}</p>
      {lines.map((line, index) => (
        <p key={index} className="text-sm leading-(--tk-line-height-normal) text-ink-muted">
          {line}
        </p>
      ))}
      {open ? (
        <div id={quotesId} className="grid gap-xs">
          {quoted.map((source, index) => (
            <p
              key={index}
              className="border-l-(length:--tk-border-thick) border-line pl-sm text-sm leading-(--tk-line-height-normal) text-ink-muted"
            >
              {quoted.length > 1 && source.date !== null ? (
                <span className="tabular-nums">{`${source.date} · `}</span>
              ) : null}
              {source.quote}
            </p>
          ))}
        </div>
      ) : null}
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

/** A scope as a small neutral chip with its icon: global, or a domain's or workstream's name. */
export function ScopeChip({ scope }: { readonly scope: PersonaScope }) {
  const text = useDictionary().memory.persona;
  const Icon = SCOPE_ICONS[scope.kind];
  return (
    <span className="inline-flex items-center gap-xs rounded-full bg-surface-raised px-sm py-xs leading-(--tk-line-height-tight)">
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
    <span
      className={`inline-flex items-center gap-xs rounded-full bg-surface-raised px-sm py-xs leading-(--tk-line-height-tight) ${
        section === null ? "text-warning" : ""
      }`}
    >
      <Icon aria-hidden className="size-(--tk-font-size-xs)" />
      {section === null ? text.otherSection(heading) : text.sections[section]}
    </span>
  );
}
