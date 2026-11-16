import { cn } from "cn";
import {
  ArrowDown,
  ArrowUp,
  Ban,
  ChevronRight,
  CircleCheck,
  CircleSlash,
  type LucideIcon,
  Search,
  SkipForward,
  SlidersHorizontal,
} from "lucide-react";
import { type PointerEvent, type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { ApiError } from "../../api/client";
import {
  type RecallTraceReport,
  type TraceResult,
  type TraceUnit,
  usePages,
  useRecallTrace,
  useWorkstreams,
} from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import { Card, CardTitle } from "../../components/ui/card";
import { DatePicker } from "../../components/ui/date-picker";
import { Input } from "../../components/ui/input";
import { Select } from "../../components/ui/select";
import { StickyBar } from "../../components/ui/sticky-bar";
import { errorTextOf } from "../../i18n/messages";
import { BusyIcon } from "../dashboard/parts";
import { sectionAnchor } from "./pages";
import { CodePill, PageTypeBadge, WorkstreamPill } from "./parts";
import { requestOf, type SearchFields, searchFieldsOf, searchParamsOf } from "./search";
import { activityPathOf } from "./timeline";
import { scoreText, type Segment, type SegmentFate, segmentsOf } from "./trace";
import { memoryPathOf } from "./views";

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

/** The three retrieval values a trace may replace. */
const OVERRIDES = ["minRerankScore", "topK", "candidates"] as const;

/** The dashed box a result sits in, as the persona's entries and the detail page's sections do. */
const RESULT_BOX = "rounded-md border-(length:--tk-border-thin) border-dashed border-ink-muted/40";

/** The columns of a unit row: what it is, then the two searches' ranks and the reranker's score. */
const UNIT_COLUMNS = "grid-cols-[minmax(0,1fr)_auto_auto_auto]";

/**
 * Where a jump to a segment lands: below the sticky search bar, whose height the bar itself keeps in
 * `--trace-bar-height`, while the page scrolls; at the top of its column once the columns scroll on their own.
 */
const BELOW_SEARCH_BAR =
  "scroll-mt-[calc(var(--trace-bar-height)+var(--tk-size-nav-bar)+var(--tk-space-xl))] lg:scroll-mt-[calc(var(--trace-bar-height)+var(--tk-space-xl))] xl:scroll-mt-sm";

/**
 * Whether the pointer itself moved: a column scrolled under a still pointer also reports it over a new row, which must
 * not take the light from the row it was scrolled to.
 */
function moved(event: PointerEvent<HTMLElement>): boolean {
  return event.movementX !== 0 || event.movementY !== 0;
}

/** The anchor of a fate's segment, for the funnel to jump to. */
function fateAnchor(fate: SegmentFate): string {
  return `trace-fate-${fate}`;
}

/**
 * The search view of the memory page: a recall trace. The conditions live in the address: submitting the bar writes them
 * there, and an address with a query runs the trace at once, so a bookmark or a link repeats it. The conditions and,
 * after a search, its funnel stay in a sticky bar; the results the agent gets sit beside every retrieval unit either
 * search returned, grouped by what became of it, and hovering one lights up the other. On the widest screens the page
 * fits the window and the two columns scroll on their own. A trace counts no return and changes no file.
 */
export function MemorySearchPage() {
  const dictionary = useDictionary();
  const [search, setSearch] = useSearchParams();
  const applied = searchFieldsOf(search);
  const request = applied.query.trim() === "" ? null : requestOf(applied);
  const trace = useRecallTrace(request);

  return (
    <div
      data-trace
      className="mx-auto grid w-full max-w-(--tk-size-page) grid-cols-1 content-start gap-md xl:h-[calc(100dvh-2*var(--tk-space-xl))] xl:grid-rows-[auto_minmax(0,1fr)]"
    >
      <h1 className="sr-only">{dictionary.memory.views.search}</h1>
      <SearchBar
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

/** What the page shows under the bar: the hint before a search, the failure, or the trace. */
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
 * The search conditions in a sticky bar: the query and its button, then the workstream, the kind, the dates and the
 * three retrieval values in a row of pills, then the funnel of the last search. The fields start from the address and
 * are sent to it on submit. A retrieval value that differs from the configured one turns its pill to the warning colour;
 * the configured value is its placeholder once a trace has said what that is. The service's problems with the fields
 * follow the rows.
 */
function SearchBar({
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
  const dictionary = useDictionary();
  const text = dictionary.memory.search;
  const [fields, setFields] = useState(applied);
  const workstreams = useWorkstreams();
  const form = useRef<HTMLFormElement>(null);
  const problems = error instanceof ApiError ? error.problems : [];
  const invalid = (path: string) => (problems.some((problem) => problem.path === path) ? true : undefined);
  const field = <K extends keyof SearchFields>(key: K, value: SearchFields[K]) =>
    setFields({ ...fields, [key]: value });

  // Keeps the bar's height where a jump to a segment reads it, since the bar grows when its rows wrap.
  useEffect(() => {
    const element = form.current;
    const page = element?.closest<HTMLElement>("[data-trace]");
    if (element === null || page === null || page === undefined) {
      return;
    }
    const observer = new ResizeObserver(() =>
      page.style.setProperty("--trace-bar-height", `${element.offsetHeight}px`),
    );
    observer.observe(element);
    return () => observer.disconnect();
  }, []);

  const names = [...(workstreams.data?.established ?? []), ...(workstreams.data?.unestablished ?? [])].map(
    (item) => item.name,
  );
  if (fields.workstream !== "" && !names.includes(fields.workstream)) {
    names.push(fields.workstream);
  }
  const kinds: readonly (readonly [SearchFields["kind"], string])[] = [
    ["", text.kinds.all],
    ["pages", text.kinds.pages],
    ["activities", text.kinds.activities],
  ];
  const labels: Readonly<Record<string, string>> = {
    query: text.queryLabel,
    workstream: text.workstreamLabel,
    kind: text.kindLabel,
    from: text.fromLabel,
    to: text.toLabel,
    ...text.overrides.labels,
  };
  const pill =
    "inline-flex cursor-pointer items-center rounded-full px-md py-xs text-sm whitespace-nowrap outline-none";

  return (
    <StickyBar role="search" aria-label={text.queryLabel}>
      <form
        ref={form}
        className="grid gap-sm"
        onSubmit={(event) => {
          event.preventDefault();
          onSubmit(fields);
        }}
      >
        <div className="flex gap-sm">
          <div className="relative min-w-0 flex-1">
            <Search
              aria-hidden
              className="pointer-events-none absolute top-1/2 left-md size-(--tk-font-size-base) -translate-y-1/2 text-ink-muted"
            />
            <Input
              type="search"
              aria-label={text.queryLabel}
              className="pl-xl"
              placeholder={text.queryPlaceholder}
              value={fields.query}
              aria-invalid={invalid("query")}
              onChange={(event) => field("query", event.target.value)}
            />
          </div>
          <Button type="submit" disabled={busy}>
            <BusyIcon busy={busy} icon={Search} />
            {text.run}
          </Button>
        </div>
        <div className="flex flex-wrap items-center gap-sm">
          <Select
            aria-label={text.workstreamLabel}
            className="w-auto"
            value={fields.workstream}
            aria-invalid={invalid("workstream")}
            onValueChange={(value) => field("workstream", value)}
            options={[{ value: "", label: text.workstreamAll }, ...names.map((name) => ({ value: name, label: name }))]}
          />
          <div role="group" aria-label={text.kindLabel} className="flex flex-wrap gap-xs">
            {kinds.map(([key, label]) => (
              <button
                key={key}
                type="button"
                aria-pressed={fields.kind === key}
                onClick={() => field("kind", key)}
                className={cn(
                  pill,
                  "transition-shadow focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
                  fields.kind === key
                    ? "bg-accent text-on-accent"
                    : "bg-surface text-ink-muted shadow-rest hover:text-ink hover:shadow-hover",
                )}
              >
                {label}
              </button>
            ))}
          </div>
          <div className="flex items-center gap-xs text-ink-muted">
            {(["from", "to"] as const).map((key, index) => (
              <span key={key} className="contents">
                {index === 0 ? null : <span aria-hidden>–</span>}
                <DatePicker
                  aria-label={labels[key]}
                  className="w-auto"
                  value={fields[key]}
                  aria-invalid={invalid(key)}
                  onValueChange={(value) => field(key, value)}
                  text={{ ...dictionary.datePicker, placeholder: labels[key]! }}
                />
              </span>
            ))}
          </div>
          <div
            role="group"
            aria-label={text.overrides.title}
            title={text.overrides.help}
            className="flex flex-wrap items-center gap-xs"
          >
            <SlidersHorizontal aria-hidden className="size-(--tk-font-size-base) text-ink-muted" />
            {OVERRIDES.map((key) => {
              const setting = report?.settings[key];
              const differs = setting !== undefined && setting.value !== setting.configured;
              return (
                <label
                  key={key}
                  title={
                    setting === undefined
                      ? text.overrides.help
                      : `${text.overrides.help} ${text.overrides.configured(scoreText(setting.configured))}`
                  }
                  className={cn(
                    pill,
                    "gap-xs pr-xs focus-within:ring-(length:--tk-border-thick) focus-within:ring-accent",
                    invalid(key)
                      ? "bg-danger-soft text-danger"
                      : differs
                        ? "bg-warning-soft text-warning"
                        : "bg-surface text-ink-muted shadow-rest",
                  )}
                >
                  {text.overrides.labels[key]}
                  <input
                    inputMode={key === "minRerankScore" ? "decimal" : "numeric"}
                    value={fields[key]}
                    placeholder={setting === undefined ? undefined : scoreText(setting.configured)}
                    aria-invalid={invalid(key)}
                    aria-describedby={differs ? `trace-${key}-differs` : undefined}
                    onChange={(event) => field(key, event.target.value)}
                    className="w-[calc(var(--tk-size-control)*1.5)] rounded-full bg-surface-raised px-sm text-center text-ink tabular-nums outline-none placeholder:text-ink-muted"
                  />
                  {differs ? (
                    <span id={`trace-${key}-differs`} className="sr-only">
                      {text.overrides.differs}
                    </span>
                  ) : null}
                </label>
              );
            })}
          </div>
        </div>
        {problems.length === 0 ? null : (
          <ul className="grid gap-xs text-sm text-danger">
            {problems.map((problem) => (
              <li key={`${problem.path}:${problem.message}`}>
                {labels[problem.path] === undefined ? problem.message : `${labels[problem.path]}：${problem.message}`}
              </li>
            ))}
          </ul>
        )}
        {report === undefined ? null : <Funnel report={report} />}
      </form>
    </StickyBar>
  );
}

/**
 * The funnel of a trace as one long pill: what each step kept, then under it the units by fate. The steps a unit can stop
 * at carry the colour of the fate of those that stopped there and jump to that segment.
 */
function Funnel({ report }: { readonly report: RecallTraceReport }) {
  const text = useDictionary().memory.search;
  const segments = segmentsOf(report.units);
  const counts = report.counts;
  const steps: readonly (readonly [string, number, SegmentFate | null])[] = [
    [text.funnel.fullText, counts.fullText, null],
    [text.funnel.vector, counts.vector, null],
    [text.funnel.fused, counts.fused, "beyondCandidates"],
    [text.funnel.reranked, counts.reranked, "belowThreshold"],
    [text.funnel.passed, counts.passed, "beyondTopK"],
    [text.funnel.results, counts.results, "shown"],
  ];
  const jump = (fate: SegmentFate) =>
    document.getElementById(fateAnchor(fate))?.scrollIntoView({ block: "start", behavior: "smooth" });

  return (
    <div
      role="group"
      aria-label={text.funnel.label}
      className="grid overflow-hidden rounded-lg bg-surface shadow-rest md:rounded-full"
    >
      <ol className="flex flex-wrap items-center gap-x-sm gap-y-xs px-md py-xs text-xs text-ink-muted">
        {steps.map(([label, value, fate], index) => {
          const segment = segments.find((item) => item.fate === fate);
          const body = (
            <>
              {fate === null ? null : (
                <span aria-hidden className={cn("size-(--tk-space-sm) rounded-full", FATE_STYLES[fate].bar)} />
              )}
              {label}
              <span className="font-semibold text-ink tabular-nums">{value}</span>
            </>
          );
          return (
            <li key={label} className="flex items-center gap-sm">
              {index === 0 ? null : <ChevronRight aria-hidden className="size-(--tk-font-size-sm)" />}
              {fate === null || segment === undefined || segment.units.length === 0 ? (
                <span className="flex items-center gap-xs">{body}</span>
              ) : (
                <button
                  type="button"
                  title={text.funnel.jump(fateTitleOf(segment, report, text), segment.units.length)}
                  onClick={() => jump(fate)}
                  className="flex cursor-pointer items-center gap-xs rounded-full px-xs outline-none hover:bg-surface-raised hover:text-ink focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
                >
                  {body}
                </button>
              )}
            </li>
          );
        })}
      </ol>
      <div aria-hidden className="flex h-xs">
        {segments.map((segment) => (
          <span
            key={segment.fate}
            className={FATE_STYLES[segment.fate].bar}
            style={{ width: `${segment.share * 100}%` }}
          />
        ))}
      </div>
    </div>
  );
}

/** A fate's name, with the threshold or candidate count it was decided by. */
function fateTitleOf(
  segment: Segment,
  report: RecallTraceReport,
  text: ReturnType<typeof useDictionary>["memory"]["search"],
): string {
  switch (segment.fate) {
    case "shown":
      return text.fates.shown;
    case "beyondTopK":
      return text.fates.beyondTopK;
    case "belowThreshold":
      return text.fates.belowThreshold(scoreText(report.settings.minRerankScore.value));
    default:
      return text.fates.beyondCandidates(report.settings.candidates.value);
  }
}

/**
 * The answer of a trace: a note when a model did not answer, then the results and the units side by side, stacked below
 * the widest breakpoint. Hovering or focusing a result lights up the units it was made from, and a unit its result; the
 * light goes out when the pointer leaves both columns.
 */
function TraceView({ report }: { readonly report: RecallTraceReport }) {
  const text = useDictionary().memory.search;
  const [lit, setLit] = useState<number | null>(null);
  const degraded = [
    report.embedding.configured && !report.embedding.answered
      ? text.degraded.embedding(report.embedding.coolingDown)
      : null,
    report.reranker.configured && !report.reranker.answered
      ? text.degraded.reranker(report.reranker.coolingDown)
      : null,
  ].filter((note) => note !== null);

  return (
    <div className="flex min-h-0 flex-col gap-md">
      {degraded.length === 0 ? null : (
        <Alert tone="warning">
          <AlertTitle>{text.degraded.title}</AlertTitle>
          <AlertDescription>
            <p>{degraded.join(" · ")}</p>
            {report.notes.length === 0 ? null : (
              <div className="mt-xs grid gap-xs">
                <p className="text-xs">{text.degraded.notes}</p>
                {report.notes.map((note) => (
                  <p key={note} className="font-mono text-xs">
                    {note}
                  </p>
                ))}
              </div>
            )}
          </AlertDescription>
        </Alert>
      )}
      <div
        onMouseLeave={() => setLit(null)}
        className="grid grid-cols-1 gap-md xl:min-h-0 xl:flex-1 xl:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)]"
      >
        <ScrollColumn lit={lit} toward="end">
          <ResultsCard report={report} lit={lit} onLight={setLit} />
        </ScrollColumn>
        <ScrollColumn lit={lit} toward="start">
          <UnitsCard report={report} lit={lit} onLight={setLit} />
        </ScrollColumn>
      </div>
    </div>
  );
}

/**
 * A column that scrolls on its own once the page fits the window, without passing the wheel on to the page at its ends.
 * When lit rows sit above or below what it shows, a pill at that edge says how many and brings the nearest into view.
 * The pills sit on the side `toward` the other column, where the pointer comes in from it.
 */
function ScrollColumn({
  lit,
  toward,
  children,
}: {
  readonly lit: number | null;
  readonly toward: "start" | "end";
  readonly children: ReactNode;
}) {
  const text = useDictionary().memory.search;
  const scroller = useRef<HTMLDivElement>(null);
  const [hidden, setHidden] = useState({ above: 0, below: 0 });

  const outside = () => {
    const element = scroller.current;
    if (element === null) {
      return { above: [], below: [] };
    }
    const box = element.getBoundingClientRect();
    const rows = [...element.querySelectorAll<HTMLElement>("[data-lit]")];
    return {
      above: rows.filter((row) => row.getBoundingClientRect().bottom <= box.top + 1),
      below: rows.filter((row) => row.getBoundingClientRect().top >= box.bottom - 1),
    };
  };

  useEffect(() => {
    const element = scroller.current;
    if (element === null) {
      return;
    }
    const measure = () => {
      const rows = outside();
      setHidden({ above: rows.above.length, below: rows.below.length });
    };
    measure();
    element.addEventListener("scroll", measure, { passive: true });
    return () => element.removeEventListener("scroll", measure);
  }, [lit]);

  const bring = (edge: "above" | "below") => {
    const rows = outside()[edge];
    rows[edge === "above" ? rows.length - 1 : 0]?.scrollIntoView({ block: "center", behavior: "smooth" });
  };
  const hint = (edge: "above" | "below") =>
    hidden[edge] === 0 ? null : (
      <button
        type="button"
        onClick={() => bring(edge)}
        className={cn(
          "absolute z-(--tk-layer-nav) flex cursor-pointer items-center gap-xs rounded-full bg-accent px-sm py-xs text-xs text-on-accent shadow-raised outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
          edge === "above" ? "top-sm" : "bottom-sm",
          toward === "start" ? "left-sm" : "right-sm",
        )}
      >
        {edge === "above" ? <ArrowUp aria-hidden className="size-(--tk-font-size-sm)" /> : null}
        {edge === "below" ? <ArrowDown aria-hidden className="size-(--tk-font-size-sm)" /> : null}
        {text.offscreen[edge](hidden[edge])}
      </button>
    );

  return (
    <div className="relative xl:min-h-0">
      <div ref={scroller} className="grid content-start xl:h-full xl:overflow-y-auto xl:overscroll-contain">
        {children}
      </div>
      {hint("above")}
      {hint("below")}
    </div>
  );
}

/** The results the agent gets, as cards or as the text itself, and how long the models took. */
function ResultsCard({
  report,
  lit,
  onLight,
}: {
  readonly report: RecallTraceReport;
  readonly lit: number | null;
  readonly onLight: (number: number | null) => void;
}) {
  const text = useDictionary().memory.search;
  const [raw, setRaw] = useState(false);
  const pages = usePages();
  const types = useMemo(
    () => new Map((pages.data?.pages ?? []).map((page) => [page.id, page.type] as const)),
    [pages.data],
  );
  const milliseconds = (value: number | null) => (value === null ? null : `${Math.round(value)} ms`);

  return (
    <Card aria-labelledby="trace-results">
      <div className="flex flex-wrap items-center justify-between gap-sm">
        <CardTitle id="trace-results">{text.resultsTitle(report.results.length)}</CardTitle>
        <div role="group" aria-label={text.view.label} className="flex rounded-full bg-surface-raised p-xs">
          {[false, true].map((option) => (
            <button
              key={String(option)}
              type="button"
              aria-pressed={raw === option}
              onClick={() => setRaw(option)}
              className={cn(
                "cursor-pointer rounded-full px-sm py-xs text-xs transition-colors outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
                raw === option ? "bg-surface font-medium text-ink shadow-rest" : "text-ink-muted hover:text-ink",
              )}
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
        <>
          <p className="text-xs text-ink-muted">{text.linkHint}</p>
          <ol className="grid gap-sm">
            {report.results.map((result) => (
              <ResultItem
                key={result.number}
                result={result}
                type={result.page === null ? null : (types.get(result.page) ?? null)}
                lit={lit === result.number}
                onLight={onLight}
              />
            ))}
          </ol>
        </>
      )}
      <p className="text-xs text-ink-muted">
        {text.timing(
          text.fullTextMethods[report.fullText],
          report.embedding.configured ? milliseconds(report.embedding.milliseconds) : null,
          report.reranker.configured ? milliseconds(report.reranker.milliseconds) : null,
        )}
      </p>
    </Card>
  );
}

/**
 * One result in a dashed box: its number, the page's type or the activity's kind, its workstream and day, its short
 * code on hover or focus, then the page's title with the sections shown, or the activity's text.
 */
function ResultItem({
  result,
  type,
  lit,
  onLight,
}: {
  readonly result: TraceResult;
  readonly type: string | null;
  readonly lit: boolean;
  readonly onLight: (number: number | null) => void;
}) {
  const text = useDictionary().memory.search;
  const page = result.kind === "page" && result.page !== null;
  return (
    <li
      data-lit={lit ? "" : undefined}
      onPointerMove={(event) => moved(event) && onLight(result.number)}
      onFocus={() => onLight(result.number)}
      className={cn(
        RESULT_BOX,
        "group grid gap-xs px-md py-sm transition-colors",
        lit && "border-accent bg-accent-soft/40",
      )}
    >
      <div className="flex flex-wrap items-center gap-sm text-xs text-ink-muted">
        <span className="grid size-(--tk-font-size-xl) shrink-0 place-items-center rounded-full bg-accent-soft font-semibold text-accent tabular-nums">
          {result.number}
        </span>
        {page ? (
          <PageTypeBadge type={type} />
        ) : (
          <Badge>{result.kind === "activity" ? text.unitKinds.activity : text.unitKinds.summary}</Badge>
        )}
        {result.workstream === "" ? null : <WorkstreamPill name={result.workstream} />}
        {result.date === null ? null : <span className="tabular-nums">{result.date}</span>}
        {result.code === null ? null : <CodePill code={result.code} reveal className="ml-auto" />}
      </div>
      {page ? (
        <>
          <Link
            to={`${memoryPathOf("pages")}/${result.page}`}
            className="font-medium underline-offset-2 outline-none hover:text-accent hover:underline focus-visible:underline"
          >
            {result.title ?? result.page}
          </Link>
          {result.sections.length === 0 ? null : (
            <p className="flex flex-wrap gap-xs">
              {result.sections.map((name) => (
                <Link
                  key={name}
                  to={`${memoryPathOf("pages")}/${result.page}#${encodeURIComponent(sectionAnchor(name))}`}
                  className="rounded-full bg-surface-raised px-sm text-xs text-ink-muted outline-none hover:text-accent focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
                >
                  {name}
                </Link>
              ))}
            </p>
          )}
        </>
      ) : (
        <Link
          to={activityPathOf({
            month: (result.date ?? "").slice(0, 7),
            day: result.kind === "activity" ? result.date : null,
            workstream: null,
          })}
          className="text-sm leading-(--tk-line-height-normal) outline-none hover:text-accent focus-visible:underline"
        >
          {result.text}
        </Link>
      )}
    </li>
  );
}

/** Every retrieval unit by fate, in one table whose columns line up across the segments. */
function UnitsCard({
  report,
  lit,
  onLight,
}: {
  readonly report: RecallTraceReport;
  readonly lit: number | null;
  readonly onLight: (number: number | null) => void;
}) {
  const text = useDictionary().memory.search;
  return (
    <Card aria-labelledby="trace-units">
      <CardTitle id="trace-units">{text.unitsTitle}</CardTitle>
      <div className={cn("grid gap-x-md", UNIT_COLUMNS)}>
        <div
          aria-hidden
          className="col-span-4 grid grid-cols-subgrid border-b-(length:--tk-border-thin) border-line px-sm pb-xs text-xs text-ink-muted"
        >
          <span />
          <span className="text-right">{text.columns.fullText}</span>
          <span className="text-right">{text.columns.vector}</span>
          <span className="text-right">{text.columns.reranker}</span>
        </div>
        {segmentsOf(report.units).map((segment) => (
          <SegmentBlock key={segment.fate} segment={segment} report={report} lit={lit} onLight={onLight} />
        ))}
      </div>
    </Card>
  );
}

/**
 * One fate's segment: its name with icon, how many units met it and their share, then the units in fused order. The two
 * segments that can run long show their first few units and unfold the rest on request.
 */
function SegmentBlock({
  segment,
  report,
  lit,
  onLight,
}: {
  readonly segment: Segment;
  readonly report: RecallTraceReport;
  readonly lit: number | null;
  readonly onLight: (number: number | null) => void;
}) {
  const text = useDictionary().memory.search;
  const [unfolded, setUnfolded] = useState(false);
  const style = FATE_STYLES[segment.fate];
  const foldable = segment.fate === "belowThreshold" || segment.fate === "beyondCandidates";
  const hidden = foldable && !unfolded ? Math.max(0, segment.units.length - FOLDED_UNITS) : 0;
  const listed = hidden > 0 ? segment.units.slice(0, FOLDED_UNITS) : segment.units;
  const percent = Math.round(segment.share * 100);
  const title = fateTitleOf(segment, report, text);
  const detail =
    segment.fate === "shown"
      ? text.shownSummary(segment.units.length, report.results.length)
      : segment.fate === "beyondCandidates" && segment.units.length > 0
        ? `${text.unitCount(segment.units.length)} · ${text.notReranked}`
        : text.unitCount(segment.units.length);

  return (
    <section aria-label={title} className="col-span-4 grid grid-cols-subgrid">
      <div
        id={fateAnchor(segment.fate)}
        className={cn(BELOW_SEARCH_BAR, "col-span-4 flex flex-wrap items-center gap-sm px-sm pt-md pb-xs")}
      >
        <span className={cn("flex items-center gap-xs text-sm font-semibold", style.text)}>
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
          <span className={cn("block h-full", style.bar)} style={{ width: `${percent}%` }} />
        </span>
      </div>
      {listed.length === 0 ? null : (
        <ul className="col-span-4 grid grid-cols-subgrid">
          {listed.map((unit) => (
            <UnitRow key={unit.fusedRank} unit={unit} lit={lit !== null && unit.result === lit} onLight={onLight} />
          ))}
        </ul>
      )}
      {hidden > 0 ? (
        <Button
          type="button"
          variant="ghost"
          className="col-span-4 justify-self-start"
          onClick={() => setUnfolded(true)}
        >
          {text.unfold(hidden)}
        </Button>
      ) : null}
    </section>
  );
}

/**
 * One unit: the number of the result it became part of, what it is and where it comes from, then its full-text rank,
 * vector rank and reranker score. Its whole text is its tooltip.
 */
function UnitRow({
  unit,
  lit,
  onLight,
}: {
  readonly unit: TraceUnit;
  readonly lit: boolean;
  readonly onLight: (number: number | null) => void;
}) {
  const text = useDictionary().memory.search;
  const [main, sub] = unitLabelOf(unit, text.unitKinds);
  const number = (value: number | null) => (value === null ? "—" : String(value));
  return (
    <li
      title={unit.text}
      data-lit={lit ? "" : undefined}
      onPointerMove={(event) => moved(event) && onLight(unit.result)}
      className={cn(
        "col-span-4 grid grid-cols-subgrid items-baseline rounded-sm px-sm py-xs text-sm transition-colors",
        lit ? "bg-accent-soft" : "hover:bg-surface-raised",
      )}
    >
      <span className="flex min-w-0 items-baseline gap-sm">
        {unit.result === null ? null : (
          <span
            aria-label={text.partOf(unit.result)}
            className="shrink-0 rounded-full bg-accent-soft px-xs text-xs font-semibold text-accent tabular-nums"
          >
            {unit.result}
          </span>
        )}
        <span className="truncate font-mono text-xs">{main}</span>
        {sub === "" ? null : <span className="shrink-0 text-xs text-ink-muted">{sub}</span>}
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
