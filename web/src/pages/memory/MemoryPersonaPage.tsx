import { ChevronDown, FolderGit2, Globe, Inbox, Tag, TriangleAlert } from "lucide-react";
import { useState } from "react";
import { useSearchParams } from "react-router";
import {
  type CandidateView,
  type FileProblem,
  type MemoryFileInfo,
  type PersonaReport,
  usePersona,
} from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import { useScrollToHash } from "../../app/use-scroll-to-hash";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import { Card, CardTitle } from "../../components/ui/card";
import { errorTextOf } from "../../i18n/messages";
import { FilePath, FileProblems } from "./parts";
import {
  type PersonaScope,
  type PersonaSectionRows,
  personaScopes,
  personaSections,
  scopeKeyOf,
  scopeParams,
} from "./persona";
import { PersonaEntryCard, SCOPE_ICONS, SECTION_ICONS, ScopeChip, type SectionKind } from "./PersonaSections";
import { occurrenceDaysOf } from "./sources";
import { CANDIDATES_ANCHOR } from "./views";

/** Clears the sticky scope bar, and the top bar too on a narrow screen, when a card is scrolled to. */
const BELOW_BARS = "scroll-mt-[calc(2*var(--tk-size-nav-bar))] lg:scroll-mt-(--tk-size-nav-bar)";

/**
 * The persona view of the memory page, in a reading column, by section: a card for each of the four sections holding
 * the entries of every scope, each marked with its scope. A sticky bar of scopes above narrows the view to one, kept in
 * the address. The candidates follow, with the rejection list folded away, and the two files' paths close the page.
 * What a file holds in the wrong form is marked where it is; the rest reads as usual.
 */
export function MemoryPersonaPage() {
  const dictionary = useDictionary();
  const text = dictionary.memory.persona;
  const persona = usePersona();
  const [params, setParams] = useSearchParams();
  useScrollToHash(persona.data !== undefined, "start");

  if (persona.isError) {
    return (
      <div className="mx-auto grid w-full max-w-(--tk-size-prompts) gap-lg">
        <h1 className="sr-only">{dictionary.memory.views.persona}</h1>
        <Alert tone="danger">
          <AlertTitle>{text.failed}</AlertTitle>
          <AlertDescription>{errorTextOf(persona.error, dictionary)}</AlertDescription>
        </Alert>
      </div>
    );
  }
  if (persona.data === undefined) {
    return <div aria-busy="true" />;
  }

  const report = persona.data;
  const scopes = personaScopes(report);
  const current = scopeKeyOf(params, report);
  const scope = scopes.find((item) => item.key === current) ?? null;
  const problems = report.personaFile.problems;
  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-prompts) gap-lg">
      <h1 className="sr-only">{dictionary.memory.views.persona}</h1>
      <div className="sticky top-(--tk-size-nav-bar) z-(--tk-layer-nav) -mx-md -mb-md grid gap-xs bg-canvas px-md py-sm lg:top-0">
        <ScopeBar
          scopes={scopes}
          current={current}
          onSelect={(key) => setParams(scopeParams(key), { replace: true })}
        />
        {scope === null ? null : <ScopeHint scope={scope} />}
      </div>
      {report.candidates.length === 0 ? null : (
        <a
          href={`#${CANDIDATES_ANCHOR}`}
          className="flex items-center gap-sm rounded-md bg-info-soft px-md py-sm text-sm text-info outline-none hover:underline focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
        >
          <Inbox aria-hidden className="size-(--tk-font-size-base) shrink-0" />
          {text.candidatesWaiting(report.candidates.length)}
        </a>
      )}
      <FileProblems problems={problems.filter(unplaced(report))} />
      {personaSections(report, current).map((group) => (
        <SectionCard key={group.section ?? "other"} group={group} problems={problems} showScope={current === null} />
      ))}
      <CandidatesCard file={report.candidatesFile} candidates={report.candidates} rejected={report.rejectionList} />
      <footer className="grid gap-xs px-md text-xs text-ink-muted">
        <span>{text.sourceFiles}</span>
        <FilePath path={report.personaFile.path} />
        <FilePath path={report.candidatesFile.path} />
      </footer>
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

/** The scopes as pills in one row that scrolls sideways when it does not fit: all, global, each domain, each workstream. */
function ScopeBar({
  scopes,
  current,
  onSelect,
}: {
  readonly scopes: readonly PersonaScope[];
  readonly current: string | null;
  readonly onSelect: (key: string | null) => void;
}) {
  const text = useDictionary().memory.persona;
  const total = scopes.reduce((sum, scope) => sum + scope.count, 0);
  return (
    <div role="group" aria-label={text.scopes} className="flex gap-sm overflow-x-auto">
      <ScopePill active={current === null} onClick={() => onSelect(null)}>
        {text.scopeAll}
        <span className="tabular-nums">{total}</span>
      </ScopePill>
      {scopes.map((scope) => {
        const missing = scope.kind === "domain" && scope.trigger === null;
        const Icon = missing ? TriangleAlert : SCOPE_ICONS[scope.kind];
        return (
          <ScopePill
            key={scope.key}
            active={current === scope.key}
            title={scope.trigger ?? undefined}
            onClick={() => onSelect(current === scope.key ? null : scope.key)}
          >
            <Icon aria-hidden className={missing && current !== scope.key ? "text-warning" : undefined} />
            {scope.name ?? text.scopeGlobal}
            <span className="tabular-nums">{scope.count}</span>
          </ScopePill>
        );
      })}
    </div>
  );
}

function ScopePill({
  active,
  title,
  onClick,
  children,
}: {
  readonly active: boolean;
  readonly title?: string;
  readonly onClick: () => void;
  readonly children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      aria-pressed={active}
      title={title}
      onClick={onClick}
      className={`inline-flex shrink-0 cursor-pointer items-center gap-xs rounded-full px-md py-xs text-sm whitespace-nowrap outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent [&_svg]:size-(--tk-font-size-sm) ${
        active ? "bg-accent text-on-accent" : "bg-surface text-ink-muted shadow-rest hover:text-ink"
      }`}
    >
      {children}
    </button>
  );
}

/** When the persona's scope is read: every session, a domain's trigger (or that it has none), one workstream. */
function ScopeHint({ scope }: { readonly scope: PersonaScope }) {
  const text = useDictionary().memory.persona;
  if (scope.kind === "domain" && scope.trigger === null) {
    return <p className="px-sm text-sm text-warning">{text.noTrigger}</p>;
  }
  return (
    <p className="px-sm text-sm text-ink-muted">
      {scope.kind === "global"
        ? text.globalHint
        : scope.kind === "domain"
          ? text.trigger(scope.trigger ?? "")
          : text.workstreamHint}
    </p>
  );
}

/** A section's card: its icon, name and count, then its entries, each with its scope unless one scope is shown. */
function SectionCard({
  group,
  problems,
  showScope,
}: {
  readonly group: PersonaSectionRows;
  readonly problems: readonly FileProblem[];
  readonly showScope: boolean;
}) {
  const text = useDictionary().memory.persona;
  const kind = group.section;
  const Icon = kind === null ? TriangleAlert : SECTION_ICONS[kind];
  const id = `persona-${kind ?? "other"}`;
  const count = group.rows.filter((row) => row.entry.code !== null).length;
  return (
    <Card aria-labelledby={id}>
      <div className="flex items-center gap-sm">
        <Icon
          aria-hidden
          className={`size-(--tk-font-size-lg) shrink-0 ${kind === null ? "text-warning" : "text-accent"}`}
        />
        <CardTitle id={id}>{kind === null ? text.otherTitle : text.sections[kind]}</CardTitle>
        <span className="text-sm text-ink-muted tabular-nums">{count}</span>
      </div>
      {group.rows.length === 0 ? (
        <p className="text-sm text-ink-muted">{text.empty}</p>
      ) : (
        <ul className="grid gap-sm">
          {group.rows.map((row) => (
            <PersonaEntryCard
              key={row.entry.line}
              entry={row.entry}
              problems={problems.filter((problem) => problem.line === row.entry.line)}
              lead={
                <>
                  {showScope ? <ScopeChip scope={row.scope} /> : null}
                  {kind === null ? <span className="text-warning">{text.otherSection(row.heading)}</span> : null}
                </>
              }
            />
          ))}
        </ul>
      )}
    </Card>
  );
}

/**
 * The candidates: each in a dashed box with its section, scope and a dot for each day it was said on above its text.
 * The rejection list follows, folded away; unfolded, it shows each record's words and marks the day of the rejection.
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
    <Card id={CANDIDATES_ANCHOR} aria-labelledby="persona-candidates" className={BELOW_BARS}>
      <div className="flex items-center gap-sm">
        <Inbox aria-hidden className="size-(--tk-font-size-lg) shrink-0 text-accent" />
        <CardTitle id="persona-candidates">{text.candidatesTitle}</CardTitle>
        <span className="text-sm text-ink-muted tabular-nums">{candidates.length}</span>
      </div>
      <FileProblems problems={file.problems} />
      {candidates.length === 0 ? (
        <p className="text-sm text-ink-muted">{text.noCandidates}</p>
      ) : (
        <ul className="grid gap-sm">
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
              <ul className="grid gap-sm">
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

/** One candidate: its section, scope and a dot per day above its text; with `quotes`, every record's words under it. */
function CandidateRow({ candidate, quotes = false }: { readonly candidate: CandidateView; readonly quotes?: boolean }) {
  const text = useDictionary().memory.persona;
  const days = occurrenceDaysOf(candidate.occurrences);
  const section = candidate.section as SectionKind;
  return (
    <li className="grid gap-xs rounded-md border-(length:--tk-border-thin) border-dashed border-ink-muted/40 px-md py-sm">
      <div className="flex flex-wrap items-center gap-sm text-xs text-ink-muted">
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
        <span>{text.days(days.length)}</span>
      </div>
      <p className="leading-(--tk-line-height-normal)">{candidate.title}</p>
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
