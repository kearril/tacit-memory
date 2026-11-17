import { cn } from "cn";
import { FolderGit2, Globe, Inbox, Tag, TriangleAlert } from "lucide-react";
import { useState } from "react";
import { useSearchParams } from "react-router";
import {
  type CandidateView,
  type FileProblem,
  type MemoryFileInfo,
  type PersonaReport,
  type PersonaSection,
  usePersona,
} from "../../api/memory";
import { QueryResult } from "../../components/query-result";
import { useDictionary } from "../../app/session-context";
import { useScrollToHash } from "../../app/use-scroll-to-hash";
import { BELOW_BARS } from "../../components/ui/sticky";
import { Segmented } from "../../components/ui/segmented";
import { StickyBar } from "../../components/ui/sticky-bar";
import { Badge } from "../../components/ui/badge";
import { Card, CardTitle } from "../../components/ui/card";
import { Chip } from "../../components/ui/chip";
import { entryBox } from "../../components/ui/entry-box";
import { FilePath, FileProblems } from "./parts";
import {
  type PersonaScope,
  type PersonaSectionRows,
  occurrenceDaysOf,
  personaScopes,
  personaSections,
  scopeKeyOf,
} from "./persona";
import { PersonaEntryCard, SCOPE_ICONS, SECTION_ICONS, ScopeChip } from "./PersonaSections";
import { CANDIDATES_ANCHOR, scopeParams } from "../../lib/routes";

/**
 * The persona view of the memory page, in a reading column, by section: a card for each of the four sections holding
 * the entries of every scope, each marked with its scope. A sticky bar of scopes above narrows the view to one, kept in
 * the address. The candidates follow, with a switch to the rejection list, and the two files' paths close the page.
 * What a file holds in the wrong form is marked where it is; the rest reads as usual.
 */
export function MemoryPersonaPage() {
  const dictionary = useDictionary();
  const persona = usePersona();
  useScrollToHash(persona.data !== undefined, "start");

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-prompts) gap-lg">
      <h1 className="sr-only">{dictionary.memory.views.persona}</h1>
      <QueryResult query={persona} title={dictionary.memory.persona.failed}>
        {(report) => <PersonaView report={report} />}
      </QueryResult>
    </div>
  );
}

function PersonaView({ report }: { readonly report: PersonaReport }) {
  const text = useDictionary().memory.persona;
  const [params, setParams] = useSearchParams();
  const scopes = personaScopes(report);
  const current = scopeKeyOf(params, report);
  const scope = scopes.find((item) => item.key === current) ?? null;
  const problems = report.personaFile.problems;
  const groups = personaSections(report, current);
  return (
    <>
      <StickyBar className="grid gap-xs">
        <ScopeBar
          scopes={scopes}
          current={current}
          onSelect={(key) => setParams(scopeParams(key), { replace: true })}
        />
        {scope === null ? null : <ScopeHint scope={scope} />}
      </StickyBar>
      {report.candidates.length === 0 ? null : (
        <a
          href={`#${CANDIDATES_ANCHOR}`}
          className="flex items-center gap-sm rounded-md bg-info-soft px-md py-sm text-sm text-info outline-none hover:underline focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
        >
          <Inbox aria-hidden className="size-(--tk-font-size-base) shrink-0" />
          {text.candidatesWaiting(report.candidates.length)}
        </a>
      )}
      <FileProblems problems={problems.filter(unplaced(groups))} />
      {groups.map((group) => (
        <SectionCard key={group.section ?? "other"} group={group} problems={problems} showScope={current === null} />
      ))}
      <CandidatesCard file={report.candidatesFile} candidates={report.candidates} rejected={report.rejectionList} />
      <footer className="grid gap-xs px-md text-xs text-ink-muted">
        <span>{text.sourceFiles}</span>
        <FilePath path={report.personaFile.path} />
        <FilePath path={report.candidatesFile.path} />
      </footer>
    </>
  );
}

/**
 * Whether a problem of `persona.md` sits on no line an entry shown in the current scope has, so it is listed apart
 * rather than in place: a problem of another scope's entry stays in sight while that scope is not shown.
 */
function unplaced(groups: readonly PersonaSectionRows[]): (problem: FileProblem) => boolean {
  const lines = new Set(groups.flatMap((group) => group.rows.map((row) => row.entry.line)));
  return (problem) => problem.line === null || !lines.has(problem.line);
}

/** The scopes as pills that wrap when they do not fit in a row: all, global, each domain, each workstream. */
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
    <div role="group" aria-label={text.scopes} className="flex flex-wrap gap-sm">
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
    <Chip pressed={active} title={title} onClick={onClick}>
      {children}
    </Chip>
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
    <Card
      aria-labelledby={id}
      variant="tab"
      label={
        <>
          <Icon
            aria-hidden
            className={`size-(--tk-font-size-base) shrink-0 ${kind === null ? "text-warning" : "text-accent"}`}
          />
          <CardTitle id={id} className="text-base">
            {kind === null ? text.otherTitle : text.sections[kind]}
          </CardTitle>
          <span className="text-sm text-ink-muted tabular-nums">{count}</span>
        </>
      }
    >
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
 * A switch in the head turns the card to the rejection list, which shows each record's words and marks the day of the
 * rejection.
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
  const [view, setView] = useState<"waiting" | "rejected">("waiting");
  const listed = view === "waiting" ? candidates : rejected;
  // Candidates waiting for review raise the card to a lead; with none it supports the sections.
  const waiting = candidates.length > 0;
  return (
    <Card
      id={CANDIDATES_ANCHOR}
      aria-labelledby="persona-candidates"
      variant={waiting ? "raised" : "flat"}
      className={BELOW_BARS}
    >
      <div className="flex flex-wrap items-center gap-sm">
        <Inbox
          aria-hidden
          className={`size-(--tk-font-size-lg) shrink-0 ${waiting ? "text-accent" : "text-ink-muted"}`}
        />
        <CardTitle id="persona-candidates">{text.candidatesTitle}</CardTitle>
        <Segmented
          className="ml-auto"
          label={text.candidateViews.label}
          value={view}
          onValueChange={setView}
          options={[
            { value: "waiting", label: text.candidateViews.waiting(candidates.length) },
            { value: "rejected", label: text.candidateViews.rejected(rejected.length) },
          ]}
        />
      </div>
      <FileProblems problems={file.problems} />
      {listed.length === 0 ? (
        <p className="text-sm text-ink-muted">{view === "waiting" ? text.noCandidates : text.noRejected}</p>
      ) : (
        <ul className="grid gap-sm">
          {listed.map((candidate, index) => (
            <CandidateRow key={`${view}-${index}`} candidate={candidate} quotes={view === "rejected"} />
          ))}
        </ul>
      )}
    </Card>
  );
}

/** One candidate: its section, scope and a dot per day above its text; with `quotes`, every record's words under it. */
function CandidateRow({ candidate, quotes = false }: { readonly candidate: CandidateView; readonly quotes?: boolean }) {
  const text = useDictionary().memory.persona;
  const days = occurrenceDaysOf(candidate.occurrences);
  const { section } = candidate;
  return (
    <li className={cn(entryBox(), "grid gap-xs")}>
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
function SectionBadge({ section }: { readonly section: PersonaSection }) {
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
