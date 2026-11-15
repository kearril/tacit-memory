import { CircleUserRound, Compass, Heart, type LucideIcon, ShieldAlert, TriangleAlert } from "lucide-react";
import type { FileProblem, PersonaEntryView, PersonaSection, PersonaSectionView } from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import { CodePill, SourceNote } from "./parts";
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

/** One section: its icon, name and entry count, then its entries; an empty one of the four says so. */
export function SectionGroup({
  section,
  problems,
  showEmpty = false,
}: {
  readonly section: PersonaSectionView;
  readonly problems: readonly FileProblem[];
  readonly showEmpty?: boolean;
}) {
  const text = useDictionary().memory.persona;
  const kind = section.section;
  const Icon = kind === null ? TriangleAlert : SECTION_ICONS[kind];
  const count = section.entries.filter((entry) => entry.code !== null).length;
  if (!showEmpty && section.entries.length === 0) {
    return null;
  }
  return (
    <section
      aria-label={kind === null ? (section.heading ?? text.beforeHeadings) : text.sections[kind]}
      className="grid gap-sm border-t-(length:--tk-border-thin) border-line pt-md first-of-type:border-t-0 first-of-type:pt-0"
    >
      <h3 className="flex items-center gap-sm text-sm font-semibold">
        <Icon
          aria-hidden
          className={`size-(--tk-font-size-base) ${kind === null ? "text-warning" : "text-ink-muted"}`}
        />
        {kind === null ? text.otherSection(section.heading) : text.sections[kind]}
        <span className="text-xs font-regular text-ink-muted">{text.count(count)}</span>
      </h3>
      {section.entries.length === 0 ? (
        <p className="text-sm text-ink-muted">{text.empty}</p>
      ) : (
        <ul className="grid gap-sm">
          {section.entries.map((entry) => (
            <EntryRow
              key={entry.line}
              entry={entry}
              problems={problems.filter((problem) => problem.line === entry.line)}
            />
          ))}
        </ul>
      )}
    </section>
  );
}

/** One entry: its text and continuation lines, its sources in small type, its short code; a problem with it under it. */
function EntryRow({
  entry,
  problems,
}: {
  readonly entry: PersonaEntryView;
  readonly problems: readonly FileProblem[];
}) {
  const text = useDictionary().memory.persona;
  const { lines, sources } = splitSources(entry.continuation);
  return (
    <li
      className={`grid grid-cols-[minmax(0,1fr)_auto] items-start gap-x-md gap-y-xs rounded-md ${
        problems.length > 0 ? "bg-warning-soft px-sm py-xs" : ""
      }`}
    >
      <div className="grid min-w-0 gap-xs">
        <p className={`leading-(--tk-line-height-normal) ${entry.code === null ? "text-ink-muted" : ""}`}>
          {entry.text}
        </p>
        {lines.map((line, index) => (
          <p key={index} className="text-sm leading-(--tk-line-height-normal) text-ink-muted">
            {line}
          </p>
        ))}
        {sources.map((source, index) => (
          <SourceNote key={index} source={source} />
        ))}
        {problems.map((problem) => (
          <p key={problem.kind} className="flex items-start gap-xs text-xs text-warning">
            <TriangleAlert aria-hidden className="mt-xs size-(--tk-font-size-xs) shrink-0" />
            {`${problem.kind}: ${problem.description}`}
          </p>
        ))}
        {entry.code === null && problems.length === 0 ? <p className="text-xs text-warning">{text.notEntry}</p> : null}
      </div>
      {entry.code === null ? null : <CodePill code={entry.code} />}
    </li>
  );
}
