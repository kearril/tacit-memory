import {
  ChevronDown,
  CircleUserRound,
  Compass,
  FolderGit2,
  Heart,
  type LucideIcon,
  ShieldAlert,
  Globe,
  Tag,
  TriangleAlert,
} from "lucide-react";
import { useState } from "react";
import {
  type CandidateView,
  type FileProblem,
  type MemoryFileInfo,
  type PersonaBlockView,
  type PersonaEntryView,
  type PersonaReport,
  type PersonaSection,
  type PersonaSectionView,
  usePersona,
} from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import { useScrollToHash } from "../../app/use-scroll-to-hash";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import { Card, CardTitle } from "../../components/ui/card";
import { errorTextOf } from "../../i18n/messages";
import { CodePill, FilePath, SourceNote } from "./parts";
import { CANDIDATES_ANCHOR } from "./views";
import { occurrenceDaysOf, splitSources } from "./sources";

/** The four persona sections. */
type SectionKind = NonNullable<PersonaSection>;

/** Each section's icon: a red line is told apart by its icon, never by the error colour. */
const SECTION_ICONS: Readonly<Record<SectionKind, LucideIcon>> = {
  redLines: ShieldAlert,
  workingRules: Compass,
  preferences: Heart,
  profile: CircleUserRound,
};

/**
 * The persona view of the memory page, in a reading column: the global persona in one card with its four sections, a
 * card for each domain and each workstream, then the candidates with the rejection list folded away. What a file holds in
 * the wrong form is marked where it is; the rest reads as usual.
 */
export function MemoryPersonaPage() {
  const dictionary = useDictionary();
  const text = dictionary.memory.persona;
  const persona = usePersona();
  useScrollToHash(persona.data !== undefined, "start");

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-prompts) gap-lg">
      <h1 className="sr-only">{dictionary.memory.views.persona}</h1>
      {persona.isError ? (
        <Alert tone="danger">
          <AlertTitle>{text.failed}</AlertTitle>
          <AlertDescription>{errorTextOf(persona.error, dictionary)}</AlertDescription>
        </Alert>
      ) : persona.data === undefined ? (
        <div aria-busy="true" />
      ) : (
        <>
          <Card aria-labelledby="persona-global">
            <CardHead id="persona-global" title={text.globalTitle} file={persona.data.personaFile} />
            <Problems file={persona.data.personaFile} only={unplaced(persona.data)} />
            {persona.data.global.map((section, index) => (
              <SectionGroup
                key={`${section.section ?? "other"}-${index}`}
                section={section}
                problems={persona.data.personaFile.problems}
                showEmpty
              />
            ))}
          </Card>
          {persona.data.domains.map((block) => (
            <BlockCard key={`domain-${block.name}`} kind="domain" block={block} file={persona.data.personaFile} />
          ))}
          {persona.data.workstreams.map((block) => (
            <BlockCard
              key={`workstream-${block.name}`}
              kind="workstream"
              block={block}
              file={persona.data.personaFile}
            />
          ))}
          <CandidatesCard
            file={persona.data.candidatesFile}
            candidates={persona.data.candidates}
            rejected={persona.data.rejectionList}
          />
        </>
      )}
    </div>
  );
}

/** Whether a problem of `persona.md` sits on no line an entry shows, so it is listed apart rather than in place. */
function unplaced(report: PersonaReport): (problem: FileProblem) => boolean {
  const lines = new Set(
    [...report.global, ...[...report.domains, ...report.workstreams].flatMap((block) => block.sections)]
      .flatMap((section) => section.entries)
      .map((entry) => entry.line),
  );
  return (problem) => problem.line === null || !lines.has(problem.line);
}

/** A card's head: its title, then the path of the file it reads with a copy button. */
function CardHead({
  id,
  title,
  detail,
  file,
}: {
  readonly id: string;
  readonly title: string;
  readonly detail?: React.ReactNode;
  readonly file: MemoryFileInfo;
}) {
  return (
    <div className="grid gap-xs">
      <div className="flex flex-wrap items-baseline gap-x-sm gap-y-xs">
        <CardTitle id={id}>{title}</CardTitle>
        {detail}
      </div>
      <FilePath path={file.path} />
    </div>
  );
}

/** A file's problems that are not on a line an entry shows, listed in the report's words. */
function Problems({
  file,
  only,
}: {
  readonly file: MemoryFileInfo;
  readonly only?: (problem: FileProblem) => boolean;
}) {
  const text = useDictionary().memory.persona;
  const problems = only === undefined ? file.problems : file.problems.filter(only);
  if (problems.length === 0) {
    return null;
  }
  return (
    <Alert tone="warning">
      <AlertTitle>{text.problemsTitle(problems.length)}</AlertTitle>
      <AlertDescription>
        <ul className="grid gap-xs">
          {problems.map((problem) => (
            <li key={`${problem.location}-${problem.kind}`}>{problemText(problem)}</li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  );
}

/** A problem as one line: where, what kind, what is wrong. */
function problemText(problem: FileProblem): string {
  return `${problem.location} · ${problem.kind}: ${problem.description}`;
}

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

/** A domain's or a workstream's block: its name, the domain's trigger and the entry count, then its sections. */
function BlockCard({
  kind,
  block,
  file,
}: {
  readonly kind: "domain" | "workstream";
  readonly block: PersonaBlockView;
  readonly file: MemoryFileInfo;
}) {
  const text = useDictionary().memory.persona;
  const count = block.sections.flatMap((section) => section.entries).filter((entry) => entry.code !== null).length;
  const id = `persona-${kind}-${block.name}`;
  const Icon = kind === "domain" ? Tag : FolderGit2;
  return (
    <Card aria-labelledby={id}>
      <div className="grid gap-xs">
        <div className="flex flex-wrap items-center gap-x-sm gap-y-xs">
          <Icon aria-hidden className="size-(--tk-font-size-lg) text-ink-muted" />
          <CardTitle id={id}>
            {kind === "domain" ? text.domainTitle(block.name) : text.workstreamTitle(block.name)}
          </CardTitle>
          <span className="text-xs text-ink-muted">{text.count(count)}</span>
        </div>
        {kind === "domain" ? (
          <p className={`text-sm ${block.trigger === null ? "text-warning" : "text-ink-muted"}`}>
            {block.trigger === null ? text.noTrigger : text.trigger(block.trigger)}
          </p>
        ) : null}
      </div>
      {block.sections.map((section, index) => (
        <SectionGroup key={`${section.section ?? "other"}-${index}`} section={section} problems={file.problems} />
      ))}
    </Card>
  );
}

/**
 * The candidates: each with its section and scope, and a dot for each day it was said on. The rejection list follows,
 * folded away; unfolded, it shows each record's words and marks the day of the rejection.
 */
function CandidatesCard({
  file,
  candidates,
  rejected,
}: {
  readonly file: MemoryFileInfo;
  readonly candidates: readonly CandidateView[];
  readonly rejected: readonly CandidateView[];
}) {
  const text = useDictionary().memory.persona;
  const [open, setOpen] = useState(false);
  return (
    <Card id={CANDIDATES_ANCHOR} aria-labelledby="persona-candidates" className="scroll-mt-lg">
      <CardHead
        id="persona-candidates"
        title={text.candidatesTitle}
        detail={<span className="text-xs text-ink-muted">{text.candidateCount(candidates.length)}</span>}
        file={file}
      />
      <Problems file={file} />
      {candidates.length === 0 ? (
        <p className="text-sm text-ink-muted">{text.noCandidates}</p>
      ) : (
        <ul className="grid gap-md">
          {candidates.map((candidate, index) => (
            <CandidateRow key={index} candidate={candidate} />
          ))}
        </ul>
      )}
      <div className="grid gap-sm border-t-(length:--tk-border-thin) border-line pt-md">
        <Button
          type="button"
          variant="ghost"
          className="justify-self-start"
          aria-expanded={open}
          aria-controls="persona-rejected"
          onClick={() => setOpen(!open)}
        >
          <ChevronDown aria-hidden className={`transition-transform ${open ? "" : "-rotate-90"}`} />
          {text.rejectedTitle(rejected.length)}
        </Button>
        {open ? (
          <div id="persona-rejected">
            {rejected.length === 0 ? (
              <p className="text-sm text-ink-muted">{text.noRejected}</p>
            ) : (
              <ul className="grid gap-md">
                {rejected.map((candidate, index) => (
                  <CandidateRow key={index} candidate={candidate} quotes />
                ))}
              </ul>
            )}
          </div>
        ) : null}
      </div>
    </Card>
  );
}

/** One candidate: its text, section and scope, a dot per day; with `quotes`, every record's words too. */
function CandidateRow({ candidate, quotes = false }: { readonly candidate: CandidateView; readonly quotes?: boolean }) {
  const text = useDictionary().memory.persona;
  const days = occurrenceDaysOf(candidate.occurrences);
  const section = candidate.section as SectionKind;
  return (
    <li className="grid gap-xs">
      <p className="leading-(--tk-line-height-normal)">{candidate.title}</p>
      <div className="flex flex-wrap items-center gap-sm">
        <SectionBadge section={section} />
        <ScopeBadge scope={candidate.scope} />
        <span role="img" aria-label={text.days(days.length)} className="flex items-center gap-xs">
          {days.map((day) => (
            <span
              key={day.date}
              title={day.rejected ? `${day.date} · ${text.rejectedMark}` : day.date}
              className={`size-(--tk-space-sm) rounded-full ${day.rejected ? "bg-ink-muted" : "bg-heat-3"}`}
            />
          ))}
        </span>
        <span className="text-xs text-ink-muted">{text.days(days.length)}</span>
      </div>
      {quotes ? (
        <ul className="grid gap-xs">
          {candidate.occurrences.map((occurrence, index) => (
            <li key={index} className="text-xs text-ink-muted">
              <span className="tabular-nums">{occurrence.date}</span>
              {occurrence.rejected ? <span className="font-medium text-ink"> · {text.rejectedMark}</span> : null}
              {" · "}
              {text.quote(occurrence.quote)}
            </li>
          ))}
        </ul>
      ) : null}
    </li>
  );
}

/** The section a candidate belongs to, with the section's icon. */
function SectionBadge({ section }: { readonly section: SectionKind }) {
  const text = useDictionary().memory.persona;
  const Icon = SECTION_ICONS[section];
  return (
    <Badge>
      <Icon aria-hidden />
      {text.sections[section]}
    </Badge>
  );
}

/** The scope a candidate goes into: global, or a workstream or domain by name, each with its icon. */
function ScopeBadge({ scope }: { readonly scope: string }) {
  const text = useDictionary().memory.persona;
  const [kind, name] = scope.includes(":")
    ? [scope.slice(0, scope.indexOf(":")), scope.slice(scope.indexOf(":") + 1)]
    : [scope, ""];
  const Icon = kind === "workstream" ? FolderGit2 : kind === "domain" ? Tag : Globe;
  return (
    <Badge tone="accent">
      <Icon aria-hidden />
      {kind === "workstream"
        ? text.workstreamTitle(name)
        : kind === "domain"
          ? text.domainTitle(name)
          : text.scopeGlobal}
    </Badge>
  );
}
