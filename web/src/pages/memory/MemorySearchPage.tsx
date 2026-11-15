import {
  Ban,
  ChevronDown,
  CircleCheck,
  CircleSlash,
  type LucideIcon,
  Search,
  SkipForward,
  SlidersHorizontal,
} from "lucide-react";
import { useState } from "react";
import { useSearchParams } from "react-router";
import { ApiError } from "../../api/client";
import { type RecallTraceReport, type TraceResult, type TraceUnit, useRecallTrace } from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Button } from "../../components/ui/button";
import { Card, CardTitle } from "../../components/ui/card";
import { Input } from "../../components/ui/input";
import { Label } from "../../components/ui/label";
import { errorTextOf } from "../../i18n/messages";
import { BusyIcon } from "../dashboard/parts";
import { hasOverrides, requestOf, type SearchFields, searchFieldsOf, searchParamsOf } from "./search";
import { scoreText, type Segment, type SegmentFate, segmentsOf } from "./trace";

/** Each fate's icon and colour: the label beside it says the fate, so the colour is never the only sign. */
const FATE_STYLES: Readonly<
  Record<SegmentFate, { readonly icon: LucideIcon; readonly text: string; readonly bar: string }>
> = {
  shown: { icon: CircleCheck, text: "text-success", bar: "bg-success" },
  beyondTopK: { icon: SkipForward, text: "text-warning", bar: "bg-warning" },
  belowThreshold: { icon: CircleSlash, text: "text-ink-muted", bar: "bg-heat-3" },
  beyondCandidates: { icon: Ban, text: "text-ink-muted", bar: "bg-heat-1" },
};

/** How many units the two long segments show before the rest is unfolded. */
const FOLDED_UNITS = 5;

/** A native select in the look of the inputs. */
const SELECT_CLASS =
  "h-control w-full min-w-0 rounded-md border-(length:--tk-border-thin) border-line bg-surface-raised px-md text-base text-ink outline-none focus-visible:border-accent focus-visible:bg-surface focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent";

/** The three retrieval values a trace may replace. */
const OVERRIDES = ["minRerankScore", "topK", "candidates"] as const;

/**
 * The search view of the memory page: a recall trace. The conditions live in the address: submitting the form writes
 * them there, and an address with a query runs the trace at once, so a bookmark or a link repeats it. The answer shows,
 * on the left, the results the agent gets and, on the right, every retrieval unit either search returned, grouped by what
 * became of it. A trace counts no return and changes no file.
 */
export function MemorySearchPage() {
  const dictionary = useDictionary();
  const [search, setSearch] = useSearchParams();
  const applied = searchFieldsOf(search);
  const request = applied.query.trim() === "" ? null : requestOf(applied);
  const trace = useRecallTrace(request);

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-page) gap-xl">
      <h1 className="sr-only">{dictionary.memory.views.search}</h1>
      <SearchForm
        key={search.toString()}
        applied={applied}
        busy={trace.isFetching}
        error={trace.error}
        report={trace.data}
        onSubmit={(fields) => {
          const next = searchParamsOf(fields);
          if (next.toString() === search.toString()) {
            void trace.refetch();
          } else {
            setSearch(next);
          }
        }}
      />
      <TraceAnswer trace={trace} />
    </div>
  );
}

/** What the page shows under the form: the hint before a search, the failure, or the trace. */
function TraceAnswer({ trace }: { readonly trace: ReturnType<typeof useRecallTrace> }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.search;
  if (trace.isError) {
    return trace.error instanceof ApiError && trace.error.problems.length > 0 ? null : (
      <Alert tone="danger">
        <AlertTitle>{text.failed}</AlertTitle>
        <AlertDescription>{errorTextOf(trace.error, dictionary)}</AlertDescription>
      </Alert>
    );
  }
  if (trace.data === undefined) {
    return trace.isFetching ? <div aria-busy="true" /> : <p className="text-sm text-ink-muted">{text.idle}</p>;
  }
  return <TraceView report={trace.data} />;
}

/**
 * The search conditions. The fields start from the address and are sent to it on submit. The three retrieval values fold
 * away under the conditions and open by themselves when the address replaces one. A value that differs from the
 * configured one is marked, with the configured value under it, once a trace has said what that is.
 */
function SearchForm({
  applied,
  busy,
  error,
  report,
  onSubmit,
}: {
  readonly applied: SearchFields;
  readonly busy: boolean;
  readonly error: Error | null;
  readonly report: RecallTraceReport | undefined;
  readonly onSubmit: (fields: SearchFields) => void;
}) {
  const text = useDictionary().memory.search;
  const [fields, setFields] = useState(applied);
  const [overridesOpen, setOverridesOpen] = useState(hasOverrides(applied));
  const problems = error instanceof ApiError ? error.problems : [];
  const problemOf = (path: string) => problems.find((problem) => problem.path === path)?.message;
  const invalid = (path: string) => (problemOf(path) === undefined ? undefined : true);
  const field = <K extends keyof SearchFields>(key: K, value: SearchFields[K]) =>
    setFields({ ...fields, [key]: value });

  return (
    <Card>
      <form
        onSubmit={(event) => {
          event.preventDefault();
          onSubmit(fields);
        }}
        className="grid gap-md md:grid-cols-2 xl:grid-cols-4"
      >
        <FormField id="trace-query" label={text.queryLabel} problem={problemOf("query")} className="col-span-full">
          <div className="flex gap-sm">
            <Input
              id="trace-query"
              type="search"
              value={fields.query}
              aria-invalid={invalid("query")}
              onChange={(event) => field("query", event.target.value)}
            />
            <Button type="submit" disabled={busy}>
              <BusyIcon busy={busy} icon={Search} />
              {text.run}
            </Button>
          </div>
        </FormField>
        <FormField id="trace-workstream" label={text.workstreamLabel} problem={problemOf("workstream")}>
          <Input
            id="trace-workstream"
            value={fields.workstream}
            placeholder={text.workstreamAll}
            aria-invalid={invalid("workstream")}
            onChange={(event) => field("workstream", event.target.value)}
          />
        </FormField>
        <FormField id="trace-kind" label={text.kindLabel} problem={problemOf("kind")}>
          <select
            id="trace-kind"
            className={SELECT_CLASS}
            value={fields.kind}
            onChange={(event) => field("kind", event.target.value as SearchFields["kind"])}
          >
            <option value="">{text.kinds.all}</option>
            <option value="pages">{text.kinds.pages}</option>
            <option value="activities">{text.kinds.activities}</option>
          </select>
        </FormField>
        <FormField id="trace-from" label={text.fromLabel} problem={problemOf("from")}>
          <Input
            id="trace-from"
            type="date"
            value={fields.from}
            aria-invalid={invalid("from")}
            onChange={(event) => field("from", event.target.value)}
          />
        </FormField>
        <FormField id="trace-to" label={text.toLabel} problem={problemOf("to")}>
          <Input
            id="trace-to"
            type="date"
            value={fields.to}
            aria-invalid={invalid("to")}
            onChange={(event) => field("to", event.target.value)}
          />
        </FormField>

        <div className="col-span-full grid gap-md">
          <Button
            type="button"
            variant="ghost"
            className="justify-self-start"
            aria-expanded={overridesOpen}
            aria-controls="trace-overrides"
            onClick={() => setOverridesOpen(!overridesOpen)}
          >
            <SlidersHorizontal aria-hidden />
            {text.overrides.title}
            <ChevronDown aria-hidden className={`transition-transform ${overridesOpen ? "" : "-rotate-90"}`} />
          </Button>
          {overridesOpen ? (
            <div id="trace-overrides" className="grid gap-md rounded-md bg-surface-raised p-md md:grid-cols-3">
              <p className="col-span-full text-sm text-ink-muted">{text.overrides.help}</p>
              {OVERRIDES.map((key) => {
                const setting = report?.settings[key];
                const differs = setting !== undefined && setting.value !== setting.configured;
                return (
                  <FormField
                    key={key}
                    id={`trace-${key}`}
                    label={text.overrides.labels[key]}
                    problem={problemOf(key)}
                    badge={differs ? text.overrides.differs : undefined}
                  >
                    <Input
                      id={`trace-${key}`}
                      inputMode={key === "minRerankScore" ? "decimal" : "numeric"}
                      value={fields[key]}
                      placeholder={setting === undefined ? undefined : scoreText(setting.configured)}
                      aria-invalid={invalid(key)}
                      onChange={(event) => field(key, event.target.value)}
                    />
                    {differs ? (
                      <p className="text-xs text-ink-muted">
                        {text.overrides.configured(scoreText(setting.configured))}
                      </p>
                    ) : null}
                  </FormField>
                );
              })}
            </div>
          ) : null}
        </div>
      </form>
    </Card>
  );
}

/** A labelled field of the form, with an optional badge beside its name and the service's problem with it under it. */
function FormField({
  id,
  label,
  problem,
  badge,
  className,
  children,
}: {
  readonly id: string;
  readonly label: string;
  readonly problem: string | undefined;
  readonly badge?: string;
  readonly className?: string;
  readonly children: React.ReactNode;
}) {
  return (
    <div className={`grid content-start gap-xs ${className ?? ""}`}>
      <div className="flex flex-wrap items-center gap-sm">
        <Label htmlFor={id}>{label}</Label>
        {badge === undefined ? null : (
          <span className="rounded-full bg-warning-soft px-sm text-xs text-warning">{badge}</span>
        )}
      </div>
      {children}
      {problem === undefined ? null : <p className="text-sm text-danger">{problem}</p>}
    </div>
  );
}

/**
 * The answer of a trace: a note when a model did not answer, then the results on the left and the units by fate on the
 * right; stacked below the widest breakpoint.
 */
function TraceView({ report }: { readonly report: RecallTraceReport }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.search;
  const [raw, setRaw] = useState(false);
  const milliseconds = (value: number | null) => (value === null ? null : `${Math.round(value)} ms`);
  const degraded = [
    report.embedding.configured && !report.embedding.answered
      ? text.degraded.embedding(report.embedding.coolingDown)
      : null,
    report.reranker.configured && !report.reranker.answered
      ? text.degraded.reranker(report.reranker.coolingDown)
      : null,
  ].filter((note) => note !== null);

  return (
    <div className="grid gap-lg">
      {degraded.length === 0 ? null : (
        <Alert tone="warning">
          <AlertTitle>{text.degraded.title}</AlertTitle>
          <AlertDescription>
            {degraded.map((note) => (
              <p key={note}>{note}</p>
            ))}
          </AlertDescription>
        </Alert>
      )}
      <div className="grid items-start gap-lg xl:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)]">
        <Card aria-labelledby="trace-results">
          <div className="flex flex-wrap items-center justify-between gap-sm">
            <CardTitle id="trace-results">{text.resultsTitle(report.results.length)}</CardTitle>
            <div role="group" aria-label={text.view.label} className="flex rounded-md bg-surface-raised p-xs">
              {[false, true].map((option) => (
                <button
                  key={String(option)}
                  type="button"
                  aria-pressed={raw === option}
                  onClick={() => setRaw(option)}
                  className={`rounded-sm px-sm py-xs text-xs transition-colors outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
                    raw === option ? "bg-surface font-medium text-ink shadow-rest" : "text-ink-muted hover:text-ink"
                  }`}
                >
                  {option ? text.view.raw : text.view.cards}
                </button>
              ))}
            </div>
          </div>
          {raw ? (
            <pre className="overflow-auto rounded-md bg-surface-raised p-md font-mono text-xs break-words whitespace-pre-wrap">
              {report.text}
            </pre>
          ) : report.results.length === 0 ? (
            <p className="text-sm text-ink-muted">{text.noResults}</p>
          ) : (
            <ol className="grid">
              {report.results.map((result) => (
                <ResultRow key={result.number} result={result} />
              ))}
            </ol>
          )}
          <p className="text-xs text-ink-muted">
            {text.timing(
              text.fullTextMethods[report.fullText],
              report.embedding.configured ? milliseconds(report.embedding.milliseconds) : null,
              report.reranker.configured ? milliseconds(report.reranker.milliseconds) : null,
            )}
          </p>
        </Card>

        <Card aria-labelledby="trace-units">
          <div className="flex flex-wrap items-baseline justify-between gap-sm">
            <CardTitle id="trace-units">{text.unitsTitle}</CardTitle>
            <ColumnHeads />
          </div>
          {segmentsOf(report.units).map((segment) => (
            <SegmentBlock key={segment.fate} segment={segment} report={report} />
          ))}
        </Card>
      </div>
    </div>
  );
}

/** One result as the agent gets it: its number, and the page it shows or the activity or summary. */
function ResultRow({ result }: { readonly result: TraceResult }) {
  const text = useDictionary().memory.search;
  return (
    <li className="grid gap-xs border-t-(length:--tk-border-thin) border-line py-sm first:border-t-0 first:pt-0">
      <div className="flex min-w-0 flex-wrap items-baseline gap-sm">
        <span className="font-mono text-xs text-ink-muted">{result.number}</span>
        {result.kind === "page" ? (
          <>
            <span className="font-mono text-sm break-all">{result.page}</span>
            <span className="text-sm">{result.title}</span>
          </>
        ) : (
          <>
            <span className="text-sm text-ink-muted">
              {result.kind === "activity" ? text.unitKinds.activity : text.unitKinds.summary} {result.date}
            </span>
            {result.code === null ? null : <span className="font-mono text-xs text-ink-muted">{result.code}</span>}
          </>
        )}
      </div>
      {result.kind === "page" ? (
        result.sections.length === 0 ? null : (
          <p className="text-xs text-ink-muted">{result.sections.map((name) => `§${name}`).join(" · ")}</p>
        )
      ) : (
        <p className="text-sm">{result.text}</p>
      )}
    </li>
  );
}

/** The column heads of the unit rows: the two searches' ranks and the reranker's score. */
function ColumnHeads() {
  const text = useDictionary().memory.search;
  return (
    <div
      aria-hidden
      className="grid grid-cols-[var(--tk-size-control)_var(--tk-size-control)_calc(var(--tk-size-control)*1.75)] gap-sm text-right text-xs text-ink-muted"
    >
      <span>{text.columns.fullText}</span>
      <span>{text.columns.vector}</span>
      <span>{text.columns.reranker}</span>
    </div>
  );
}

/**
 * One fate's segment: its name with icon, how many units met it and their share, then the units in fused order. The two
 * segments that can run long show their first few units and unfold the rest on request.
 */
function SegmentBlock({ segment, report }: { readonly segment: Segment; readonly report: RecallTraceReport }) {
  const text = useDictionary().memory.search;
  const [unfolded, setUnfolded] = useState(false);
  const style = FATE_STYLES[segment.fate];
  const foldable = segment.fate === "belowThreshold" || segment.fate === "beyondCandidates";
  const hidden = foldable && !unfolded ? Math.max(0, segment.units.length - FOLDED_UNITS) : 0;
  const listed = hidden > 0 ? segment.units.slice(0, FOLDED_UNITS) : segment.units;
  const percent = Math.round(segment.share * 100);
  const title =
    segment.fate === "shown"
      ? text.fates.shown
      : segment.fate === "beyondTopK"
        ? text.fates.beyondTopK
        : segment.fate === "belowThreshold"
          ? text.fates.belowThreshold(scoreText(report.settings.minRerankScore.value))
          : text.fates.beyondCandidates(report.settings.candidates.value);
  const detail =
    segment.fate === "shown"
      ? text.shownSummary(segment.units.length, report.results.length)
      : segment.fate === "beyondCandidates" && segment.units.length > 0
        ? `${text.unitCount(segment.units.length)} · ${text.notReranked}`
        : text.unitCount(segment.units.length);

  return (
    <section
      aria-label={title}
      className="grid gap-xs border-t-(length:--tk-border-thin) border-line pt-sm first-of-type:border-t-0"
    >
      <div className="flex flex-wrap items-center gap-sm">
        <span className={`flex items-center gap-xs text-sm font-semibold ${style.text}`}>
          <style.icon aria-hidden className="size-(--tk-font-size-base)" />
          {title}
        </span>
        <span className="text-xs text-ink-muted">{detail}</span>
        <span
          role="img"
          aria-label={text.share(percent)}
          title={text.share(percent)}
          className="ml-auto h-xs w-(--tk-size-sidebar) max-w-1/3 overflow-hidden rounded-full bg-heat-0"
        >
          <span className={`block h-full ${style.bar}`} style={{ width: `${percent}%` }} />
        </span>
      </div>
      {listed.length === 0 ? null : (
        <ul className="grid">
          {listed.map((unit) => (
            <UnitRow key={`${unit.fusedRank}`} unit={unit} />
          ))}
        </ul>
      )}
      {hidden > 0 ? (
        <Button type="button" variant="ghost" className="justify-self-start" onClick={() => setUnfolded(true)}>
          {text.unfold(hidden)}
        </Button>
      ) : null}
    </section>
  );
}

/** One unit: what it is and where it comes from, then its full-text rank, vector rank and reranker score. */
function UnitRow({ unit }: { readonly unit: TraceUnit }) {
  const text = useDictionary().memory.search;
  const [main, sub] = unitLabelOf(unit, text.unitKinds);
  const number = (value: number | null) => (value === null ? "—" : String(value));
  return (
    <li className="grid grid-cols-[minmax(0,1fr)_var(--tk-size-control)_var(--tk-size-control)_calc(var(--tk-size-control)*1.75)] items-baseline gap-sm rounded-md px-sm py-xs text-sm hover:bg-surface-raised">
      <span className="min-w-0 truncate" title={unit.text}>
        <span className="font-mono">{main}</span> <span className="text-xs text-ink-muted">{sub}</span>
      </span>
      <span className="text-right font-mono text-xs text-ink-muted tabular-nums">{number(unit.fullTextRank)}</span>
      <span className="text-right font-mono text-xs text-ink-muted tabular-nums">{number(unit.vectorRank)}</span>
      <span className="text-right font-mono text-xs text-ink-muted tabular-nums">
        {unit.rerankScore === null ? "—" : scoreText(unit.rerankScore)}
      </span>
    </li>
  );
}

/** A unit's name and the detail beside it: a page's section or clue, an activity's code and day, a summary's period. */
function unitLabelOf(
  unit: TraceUnit,
  kinds: {
    readonly clue: string;
    readonly activity: string;
    readonly summary: string;
    readonly week: string;
    readonly month: string;
  },
): [string, string] {
  switch (unit.kind) {
    case "activity":
      return [`${kinds.activity} ${unit.code ?? ""}`, unit.date ?? ""];
    case "summary":
      return [`${kinds.summary} ${unit.date ?? ""}`, unit.section === "month" ? kinds.month : kinds.week];
    case "clue":
      return [unit.source, kinds.clue];
    default:
      return [unit.source, `§${unit.section ?? ""}`];
  }
}
