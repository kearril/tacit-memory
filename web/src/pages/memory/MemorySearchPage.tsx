import { cn } from "cn";
import {
  ArrowDown,
  ArrowUp,
  Ban,
  ChevronDown,
  ChevronRight,
  CircleCheck,
  CircleSlash,
  GitBranch,
  Layers,
  ListFilter,
  type LucideIcon,
  Search,
  SkipForward,
  SlidersHorizontal,
  TrendingDown,
} from "lucide-react";
import { type PointerEvent, type ReactNode, useEffect, useMemo, useRef, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { ApiError } from "../../api/client";
import {
  type PageListItem,
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
import { Popover, PopoverContent, PopoverTrigger } from "../../components/ui/popover";
import { Segmented } from "../../components/ui/segmented";
import { Select } from "../../components/ui/select";
import { barBottom, scrollUnderBar, UNDER_BAR, useStickUnderBar } from "../../components/ui/sticky";
import { StickyBar } from "../../components/ui/sticky-bar";
import { Chip } from "../../components/ui/chip";
import { chipVariants } from "../../components/ui/chip-variants";
import { entryBox } from "../../components/ui/entry-box";
import { errorTextOf } from "../../i18n/messages";
import { BusyIcon } from "../../components/ui/busy-icon";
import { scrollBehavior } from "../../lib/motion";
import { sectionLabel } from "./pages";
import { CodePill, PageTypeBadge, WorkstreamPill } from "./parts";
import { requestOf } from "./search";
import { scoreText, type Segment, type SegmentFate, segmentsOf } from "./trace";
import { activityPathOf, pagePathOf, type SearchFields, searchFieldsOf, searchParamsOf } from "../../lib/routes";

/** Each fate's icon and colour: the label beside it says the fate, so the colour is never the only sign. */
const FATE_STYLES: Readonly<
  Record<SegmentFate, { readonly icon: LucideIcon; readonly text: string; readonly bar: string }>
> = {
  shown: { icon: CircleCheck, text: "text-success", bar: "bg-success" },
  beyondTopK: { icon: SkipForward, text: "text-warning", bar: "bg-warning" },
  belowRelativeFloor: { icon: TrendingDown, text: "text-warning", bar: "bg-heat-4" },
  beyondActivityCap: { icon: Layers, text: "text-info", bar: "bg-info" },
  belowThreshold: { icon: CircleSlash, text: "text-ink-muted", bar: "bg-heat-3" },
  beyondCandidates: { icon: Ban, text: "text-ink-muted", bar: "bg-heat-1" },
};

/** How many units the two long segments show before the rest is unfolded. */
const FOLDED_UNITS = 5;

/** The three retrieval values a trace may replace. */
const OVERRIDES = ["minRerankScore", "topK", "candidates"] as const;

/** The conditions besides the query, counted on the button that unfolds them while the bar is stuck. */
const CONDITIONS = ["workstream", "kind", "from", "to", ...OVERRIDES] as const;

/** The columns of a unit row: what it is, then the two searches' ranks, the raw rerank score and the adjusted final score. */
const UNIT_COLUMNS = "grid-cols-[minmax(0,1fr)_auto_auto_auto_auto]";

/**
 * Whether the pointer itself moved: a page scrolled under a still pointer also reports it over a new row, which must not
 * take the light from the row it was scrolled to.
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
 * after a search, its funnel float in a sticky bar over the page; the results the agent gets sit beside every retrieval
 * unit either search returned, grouped by what became of it, and hovering one lights up the other. The page scrolls as a
 * whole. A trace counts no return and changes no file.
 */
export function MemorySearchPage() {
  const dictionary = useDictionary();
  const [search, setSearch] = useSearchParams();
  const applied = searchFieldsOf(search);
  const request = applied.query.trim() === "" ? null : requestOf(applied);
  const trace = useRecallTrace(request);
  const searchKey = search.toString();

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-page) grid-cols-1 content-start gap-md">
      <h1 className="sr-only">{dictionary.memory.views.search}</h1>
      <SearchBar
        key={searchKey}
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
 * The search conditions in a sticky bar: the query and its button; the workstream, the kind, the dates and the button of
 * the retrieval values; then the funnel of the last search. Once the bar floats, the second row folds away behind a
 * button that counts the conditions set, so the bar keeps to the query and the funnel. The fields start from the address
 * and are sent to it on submit. The service's problems with the fields follow the rows.
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
  const [unfolded, setUnfolded] = useState(false);
  const workstreams = useWorkstreams();
  const problems = error instanceof ApiError ? error.problems : [];
  const invalid = (path: string) => (problems.some((problem) => problem.path === path) ? true : undefined);
  const field = <K extends keyof SearchFields>(key: K, value: SearchFields[K]) =>
    setFields({ ...fields, [key]: value });

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
  const set = CONDITIONS.filter((key) => fields[key].trim() !== "").length;

  return (
    <StickyBar role="search" aria-label={text.queryLabel}>
      {(stuck) => {
        const folded = stuck && !unfolded && problems.length === 0;
        return (
          <form
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
              {stuck ? (
                <Button
                  type="button"
                  variant="outline"
                  aria-expanded={!folded}
                  aria-controls="trace-conditions"
                  onClick={() => setUnfolded(folded)}
                >
                  <ListFilter aria-hidden />
                  <span className="hidden sm:inline">{text.conditions(set)}</span>
                  <ChevronDown aria-hidden className={cn("transition-transform", !folded && "rotate-180")} />
                </Button>
              ) : null}
              <Button type="submit" disabled={busy}>
                <BusyIcon busy={busy} icon={Search} />
                {text.run}
              </Button>
            </div>
            {folded ? null : (
              <div id="trace-conditions" className="flex flex-wrap items-center gap-sm">
                <Select
                  aria-label={text.workstreamLabel}
                  className="w-auto"
                  value={fields.workstream}
                  aria-invalid={invalid("workstream")}
                  onValueChange={(value) => field("workstream", value)}
                  options={[
                    { value: "", label: text.workstreamAll },
                    ...names.map((name) => ({ value: name, label: name })),
                  ]}
                />
                <div role="group" aria-label={text.kindLabel} className="flex flex-wrap gap-xs">
                  {kinds.map(([key, label]) => (
                    <Chip key={key} pressed={fields.kind === key} onClick={() => field("kind", key)}>
                      {label}
                    </Chip>
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
                <Overrides
                  fields={fields}
                  report={report}
                  invalid={OVERRIDES.some((key) => invalid(key))}
                  onChange={(key, value) => field(key, value)}
                />
              </div>
            )}
            {problems.length === 0 ? null : (
              <ul className="grid gap-xs text-sm text-danger">
                {problems.map((problem) => (
                  <li key={`${problem.path}:${problem.message}`}>
                    {labels[problem.path] === undefined
                      ? problem.message
                      : `${labels[problem.path]}：${problem.message}`}
                  </li>
                ))}
              </ul>
            )}
            {report === undefined ? null : <Funnel report={report} />}
          </form>
        );
      }}
    </StickyBar>
  );
}

/**
 * The three retrieval values behind one pill that opens a panel: each value by name, its input with the configured value
 * as placeholder, and the configured value under one that replaces it; what replacing means closes the panel. The pill
 * counts the values replaced, in the warning colour, or turns to the danger colour when the service refused one.
 */
function Overrides({
  fields,
  report,
  invalid,
  onChange,
}: {
  readonly fields: SearchFields;
  readonly report: RecallTraceReport | undefined;
  readonly invalid: boolean;
  readonly onChange: (key: (typeof OVERRIDES)[number], value: string) => void;
}) {
  const text = useDictionary().memory.search;
  const replaced = OVERRIDES.filter((key) => fields[key].trim() !== "").length;
  return (
    <Popover>
      <PopoverTrigger asChild>
        <button
          type="button"
          className={cn(
            chipVariants({ tone: invalid ? "danger" : replaced > 0 ? "warning" : "rest" }),
            "group ml-auto",
          )}
        >
          <SlidersHorizontal aria-hidden />
          {text.overrides.pill(replaced)}
          <ChevronDown aria-hidden className="transition-transform group-data-[state=open]:rotate-180" />
        </button>
      </PopoverTrigger>
      <PopoverContent align="end" aria-label={text.overrides.title} className="w-max gap-md">
        {OVERRIDES.map((key) => {
          const setting = report?.settings[key];
          const value = fields[key];
          return (
            <label key={key} className="grid grid-cols-[minmax(0,1fr)_auto] items-center gap-x-md gap-y-xs">
              <span className="whitespace-nowrap">{text.overrides.labels[key]}</span>
              <Input
                inputMode={key === "minRerankScore" ? "decimal" : "numeric"}
                className="w-[calc(var(--tk-size-control)*2)] text-right tabular-nums"
                value={value}
                placeholder={setting === undefined ? undefined : scoreText(setting.configured)}
                onChange={(event) => onChange(key, event.target.value)}
              />
              {value.trim() === "" || setting === undefined ? null : (
                <span className="col-span-2 text-right text-xs text-warning">
                  {text.overrides.configured(scoreText(setting.configured))}
                </span>
              )}
            </label>
          );
        })}
        <p className="max-w-(--tk-size-sidebar) border-t-(length:--tk-border-thin) border-line pt-sm text-xs text-ink-muted">
          {text.overrides.help}
        </p>
      </PopoverContent>
    </Popover>
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
  const jump = (fate: SegmentFate) => {
    const segment = document.getElementById(fateAnchor(fate));
    if (segment !== null) {
      scrollUnderBar(segment);
    }
  };

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
    case "belowRelativeFloor":
      return text.fates.belowRelativeFloor;
    case "beyondActivityCap":
      return text.fates.beyondActivityCap;
    case "belowThreshold":
      return text.fates.belowThreshold(scoreText(report.settings.minRerankScore.value));
    default:
      return text.fates.beyondCandidates(report.settings.candidates.value);
  }
}

/**
 * The answer of a trace: a note when a model did not answer, then the results and the units side by side, stacked below
 * the widest breakpoint. Hovering or focusing a result lights up the units it was made from, and a unit its result; the
 * light goes out when the pointer leaves both columns. Side by side, the results follow the page under the bar.
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
    <>
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
        className="grid grid-cols-1 items-start gap-md xl:grid-cols-[minmax(0,1fr)_minmax(0,1.2fr)]"
      >
        <Column lit={lit} sticky toward="end">
          <ResultsCard report={report} lit={lit} onLight={setLit} />
        </Column>
        <Column lit={lit} sticky={false} toward="start">
          <UnitsCard report={report} lit={lit} onLight={setLit} />
        </Column>
      </div>
    </>
  );
}

/**
 * One column of the answer, in the page's own scroll. A sticky one, once the columns stand side by side, stays under the
 * bar while it is shorter than the window; a taller one scrolls along until its end shows, then stays. When lit rows sit
 * above the bar or below the window, a pill at that edge says how many and brings the nearest into view. The pills sit
 * on the side `toward` the other column, where the pointer comes in from it.
 */
function Column({
  lit,
  sticky,
  toward,
  children,
}: {
  readonly lit: number | null;
  readonly sticky: boolean;
  readonly toward: "start" | "end";
  readonly children: ReactNode;
}) {
  const text = useDictionary().memory.search;
  const column = useRef<HTMLDivElement>(null);
  const [hidden, setHidden] = useState({ above: 0, below: 0 });

  useStickUnderBar(column, sticky);

  const outside = () => {
    const element = column.current;
    if (element === null) {
      return { above: [], below: [] };
    }
    const top = barBottom();
    const rows = [...element.querySelectorAll<HTMLElement>("[data-lit]")];
    return {
      above: rows.filter((row) => row.getBoundingClientRect().bottom <= top),
      below: rows.filter((row) => row.getBoundingClientRect().top >= window.innerHeight),
    };
  };

  useEffect(() => {
    const measure = () => {
      const rows = outside();
      setHidden({ above: rows.above.length, below: rows.below.length });
    };
    measure();
    window.addEventListener("scroll", measure, { passive: true });
    return () => window.removeEventListener("scroll", measure);
  }, [lit]);

  const hint = (edge: "above" | "below") =>
    hidden[edge] === 0 ? null : (
      <div className={cn("sticky z-(--tk-layer-nav) h-0", edge === "above" ? UNDER_BAR : "bottom-md")}>
        <button
          type="button"
          onClick={() => {
            const rows = outside()[edge];
            rows[edge === "above" ? rows.length - 1 : 0]?.scrollIntoView({
              block: "center",
              behavior: scrollBehavior(),
            });
          }}
          className={cn(
            "absolute flex cursor-pointer items-center gap-xs rounded-full bg-accent px-sm py-xs text-xs whitespace-nowrap text-on-accent shadow-raised outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
            edge === "above" ? "top-0" : "bottom-0",
            toward === "start" ? "left-sm" : "right-sm",
          )}
        >
          {edge === "above" ? (
            <ArrowUp aria-hidden className="size-(--tk-font-size-sm)" />
          ) : (
            <ArrowDown aria-hidden className="size-(--tk-font-size-sm)" />
          )}
          {text.offscreen[edge](hidden[edge])}
        </button>
      </div>
    );

  return (
    <div ref={column} className={cn("grid content-start", sticky && "xl:sticky")}>
      {hint("above")}
      {children}
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
  const listed = useMemo(
    () => new Map((pages.data?.pages ?? []).map((page) => [page.id, page] as const)),
    [pages.data],
  );
  const milliseconds = (value: number | null) => (value === null ? null : `${Math.round(value)} ms`);

  return (
    <Card aria-labelledby="trace-results">
      <div className="flex flex-wrap items-center justify-between gap-sm">
        <CardTitle id="trace-results">{text.resultsTitle(report.results.length)}</CardTitle>
        <Segmented
          label={text.view.label}
          value={raw ? "raw" : "cards"}
          onValueChange={(value) => setRaw(value === "raw")}
          options={[
            { value: "cards", label: text.view.cards },
            { value: "raw", label: text.view.raw },
          ]}
        />
      </div>
      {raw ? (
        <pre className="overflow-auto rounded-md bg-surface-raised p-md font-mono text-xs break-words whitespace-pre-wrap">
          {report.text}
        </pre>
      ) : report.results.length === 0 ? (
        <p className="text-sm text-ink-muted">{text.noResults}</p>
      ) : (
        <ol className="grid gap-sm">
          {report.results.map((result) => (
            <ResultItem
              key={result.number}
              result={result}
              listed={result.page === null ? undefined : listed.get(result.page)}
              lit={lit === result.number}
              onLight={onLight}
            />
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
  );
}

/**
 * One result in a dashed box: its number, the page's type or the activity's kind, its workstream and day, its short
 * code on hover or focus, then the page's title with the sections shown, or the activity's text.
 */
function ResultItem({
  result,
  listed,
  lit,
  onLight,
}: {
  readonly result: TraceResult;
  /** The page in the list, for its type; `undefined` until the list is read, or for a result that is not a page. */
  readonly listed: PageListItem | undefined;
  readonly lit: boolean;
  readonly onLight: (number: number | null) => void;
}) {
  const dictionary = useDictionary().memory;
  const text = dictionary.search;
  const pageId = result.kind === "page" ? result.page : null;
  return (
    <li
      data-lit={lit ? "" : undefined}
      onPointerMove={(event) => moved(event) && onLight(result.number)}
      onFocus={() => onLight(result.number)}
      className={cn(entryBox({ tone: lit ? "lit" : "quiet" }), "group grid gap-xs transition-colors")}
    >
      <div className="flex flex-wrap items-center gap-sm text-xs text-ink-muted">
        <span className="grid size-(--tk-font-size-xl) shrink-0 place-items-center rounded-full bg-accent-soft font-semibold text-accent tabular-nums">
          {result.number}
        </span>
        {pageId !== null ? (
          <>
            <PageTypeBadge type={result.pageType ?? listed?.type ?? null} written={listed?.writtenType} />
            {result.overturned ? <Badge tone="warning">{text.overturned}</Badge> : null}
          </>
        ) : (
          <Badge>{result.kind === "activity" ? text.unitKinds.activity : text.unitKinds.summary}</Badge>
        )}
        {result.workstream === "" ? null : <WorkstreamPill name={result.workstream} />}
        {result.date === null ? null : <span className="tabular-nums">{result.date}</span>}
        {result.code === null ? null : <CodePill code={result.code} reveal className="ml-auto" />}
      </div>
      {pageId !== null ? (
        <>
          <Link
            to={pagePathOf(pageId)}
            className="font-medium underline-offset-2 outline-none hover:text-accent hover:underline focus-visible:underline"
          >
            {result.title ?? pageId}
          </Link>
          {result.supersededBy !== null && result.supersededBy !== undefined && result.supersededBy.length > 0 ? (
            <p className="flex flex-wrap items-center gap-xs text-xs text-warning">
              <span>{text.supersededBy(result.supersededBy.join(", "))}</span>
            </p>
          ) : null}
          {result.sections.length === 0 ? null : (
            <p className="flex flex-wrap gap-xs">
              {result.sections.map((section) => (
                <Link
                  key={section.name}
                  to={pagePathOf(pageId, section.name)}
                  className="rounded-full bg-surface-raised px-sm text-xs text-ink-muted outline-none hover:text-accent focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
                >
                  {sectionLabel(section, dictionary.pages.sectionNames)}
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
    <Card variant="flat" aria-labelledby="trace-units">
      <CardTitle id="trace-units">{text.unitsTitle}</CardTitle>
      <div className={cn("grid gap-x-md", UNIT_COLUMNS)}>
        <div
          aria-hidden
          className="col-span-5 grid grid-cols-subgrid border-b-(length:--tk-border-thin) border-line px-sm pb-xs text-xs text-ink-muted"
        >
          <span />
          <span className="text-right">{text.columns.fullText}</span>
          <span className="text-right">{text.columns.vector}</span>
          <span className="text-right">{text.columns.reranker}</span>
          <span className="text-right">{text.columns.finalScore}</span>
        </div>
        {segmentsOf(report.units).map((segment) => (
          <SegmentBlock key={segment.fate} segment={segment} report={report} lit={lit} onLight={onLight} />
        ))}
      </div>
    </Card>
  );
}

/**
 * One fate's segment: its name with icon, how many units met it and their share, then the units in fused order. Long
 * non-shown segments show their first few units and unfold the rest on request.
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
  const foldable =
    segment.fate === "belowRelativeFloor" ||
    segment.fate === "beyondActivityCap" ||
    segment.fate === "belowThreshold" ||
    segment.fate === "beyondCandidates";
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
    <section aria-label={title} className="col-span-5 grid grid-cols-subgrid">
      <div id={fateAnchor(segment.fate)} className="col-span-5 flex flex-wrap items-center gap-sm px-sm pt-md pb-xs">
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
        <ul className="col-span-5 grid grid-cols-subgrid">
          {listed.map((unit) => (
            <UnitRow key={unit.fusedRank} unit={unit} lit={lit !== null && unit.result === lit} onLight={onLight} />
          ))}
        </ul>
      )}
      {hidden > 0 ? (
        <Button
          type="button"
          variant="ghost"
          className="col-span-5 justify-self-start"
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
 * vector rank, raw reranker score and adjusted final score. Its whole text is its tooltip.
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
  const originLabel =
    unit.graphOrigin === "supersedes"
      ? text.graphOrigins.supersedes
      : unit.graphOrigin === "related"
        ? text.graphOrigins.related
        : null;
  return (
    <li
      title={unit.text}
      data-lit={lit ? "" : undefined}
      onPointerMove={(event) => moved(event) && onLight(unit.result)}
      className={cn(
        "col-span-5 grid grid-cols-subgrid items-baseline rounded-sm px-sm py-xs text-sm transition-colors",
        lit ? "bg-accent-soft" : "hover:bg-surface-raised",
      )}
    >
      <span className="flex min-w-0 flex-wrap items-center gap-xs">
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
        {originLabel === null ? null : (
          <Badge tone="info">
            <GitBranch aria-hidden />
            {originLabel}
          </Badge>
        )}
        {unit.overturned ? <Badge tone="warning">{text.overturned}</Badge> : null}
      </span>
      <span className="text-right font-mono text-xs text-ink-muted tabular-nums">{number(unit.fullTextRank)}</span>
      <span className="text-right font-mono text-xs text-ink-muted tabular-nums">{number(unit.vectorRank)}</span>
      <span className="text-right font-mono text-xs text-ink-muted tabular-nums">
        {unit.rerankScore === null ? "—" : scoreText(unit.rerankScore)}
      </span>
      <span className="text-right font-mono text-xs font-medium tabular-nums">
        {unit.finalScore === null || unit.finalScore === undefined ? "—" : scoreText(unit.finalScore)}
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
