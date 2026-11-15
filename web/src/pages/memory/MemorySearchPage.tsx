import { useMutation } from "@tanstack/react-query";
import { Ban, CircleCheck, CircleSlash, type LucideIcon, Search, SkipForward } from "lucide-react";
import { useState } from "react";
import { ApiError } from "../../api/client";
import {
  type RecallTraceReport,
  type RecallTraceRequest,
  type TraceResult,
  type TraceUnit,
  traceRecall,
} from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Button } from "../../components/ui/button";
import { Card, CardTitle } from "../../components/ui/card";
import { Input } from "../../components/ui/input";
import { Label } from "../../components/ui/label";
import { errorTextOf } from "../../i18n/messages";
import { BusyIcon } from "../dashboard/parts";
import { scoreText, type Segment, type SegmentFate, segmentsOf } from "./trace";

/** The search form's fields, as typed; empty means left out. */
interface SearchFields {
  readonly query: string;
  readonly workstream: string;
  readonly from: string;
  readonly to: string;
  readonly kind: "" | "pages" | "activities";
}

const EMPTY_FIELDS: SearchFields = { query: "", workstream: "", from: "", to: "", kind: "" };

/** Each fate's icon and colour: the label beside it says the fate, so the colour is never the only sign. */
const FATE_STYLES: Readonly<
  Record<SegmentFate, { readonly icon: LucideIcon; readonly text: string; readonly bar: string }>
> = {
  shown: { icon: CircleCheck, text: "text-success", bar: "bg-success" },
  beyondTopK: { icon: SkipForward, text: "text-warning", bar: "bg-warning" },
  belowThreshold: { icon: CircleSlash, text: "text-ink-muted", bar: "bg-heat-3" },
  beyondCandidates: { icon: Ban, text: "text-ink-muted", bar: "bg-heat-1" },
};

/** A native select in the look of the inputs. */
const SELECT_CLASS =
  "h-control w-full min-w-0 rounded-md border-(length:--tk-border-thin) border-line bg-surface-raised px-md text-base text-ink outline-none focus-visible:border-accent focus-visible:bg-surface focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent";

/**
 * The search view of the memory page: a recall trace. The form takes what `recall` takes for a search; the answer shows,
 * on the left, the results the agent gets and, on the right, every retrieval unit either search returned, grouped by
 * what became of it. A trace counts no return and changes no file.
 */
export function MemorySearchPage() {
  const dictionary = useDictionary();
  const text = dictionary.memory.search;
  const [fields, setFields] = useState<SearchFields>(EMPTY_FIELDS);
  const trace = useMutation({ mutationFn: traceRecall });
  const problems = trace.error instanceof ApiError ? trace.error.problems : [];
  const problemOf = (path: string) => problems.find((problem) => problem.path === path)?.message;

  const field = <K extends keyof SearchFields>(key: K, value: SearchFields[K]) =>
    setFields({ ...fields, [key]: value });

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-page) gap-xl">
      <h1 className="sr-only">{dictionary.memory.views.search}</h1>
      <Card>
        <form
          onSubmit={(event) => {
            event.preventDefault();
            trace.mutate(requestOf(fields));
          }}
          className="grid gap-md md:grid-cols-2 xl:grid-cols-4"
        >
          <FormField id="trace-query" label={text.queryLabel} problem={problemOf("query")} className="col-span-full">
            <div className="flex gap-sm">
              <Input
                id="trace-query"
                type="search"
                value={fields.query}
                aria-invalid={problemOf("query") === undefined ? undefined : true}
                onChange={(event) => field("query", event.target.value)}
              />
              <Button type="submit" disabled={trace.isPending}>
                <BusyIcon busy={trace.isPending} icon={Search} />
                {text.run}
              </Button>
            </div>
          </FormField>
          <FormField id="trace-workstream" label={text.workstreamLabel} problem={problemOf("workstream")}>
            <Input
              id="trace-workstream"
              value={fields.workstream}
              placeholder={text.workstreamAll}
              aria-invalid={problemOf("workstream") === undefined ? undefined : true}
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
              aria-invalid={problemOf("from") === undefined ? undefined : true}
              onChange={(event) => field("from", event.target.value)}
            />
          </FormField>
          <FormField id="trace-to" label={text.toLabel} problem={problemOf("to")}>
            <Input
              id="trace-to"
              type="date"
              value={fields.to}
              aria-invalid={problemOf("to") === undefined ? undefined : true}
              onChange={(event) => field("to", event.target.value)}
            />
          </FormField>
        </form>
      </Card>

      {trace.isError && problems.length === 0 ? (
        <Alert tone="danger">
          <AlertTitle>{text.failed}</AlertTitle>
          <AlertDescription>{errorTextOf(trace.error, dictionary)}</AlertDescription>
        </Alert>
      ) : null}

      {trace.data === undefined ? (
        trace.isError ? null : (
          <p className="text-sm text-ink-muted">{text.idle}</p>
        )
      ) : (
        <TraceView report={trace.data} />
      )}
    </div>
  );
}

/** The request for the typed fields: an empty field is left out. */
function requestOf(fields: SearchFields): RecallTraceRequest {
  const optional = (value: string) => (value.trim() === "" ? null : value.trim());
  return {
    query: fields.query,
    workstream: optional(fields.workstream),
    from: optional(fields.from),
    to: optional(fields.to),
    kind: optional(fields.kind),
  };
}

/** A labelled field of the form, with the service's problem with it under it. */
function FormField({
  id,
  label,
  problem,
  className,
  children,
}: {
  readonly id: string;
  readonly label: string;
  readonly problem: string | undefined;
  readonly className?: string;
  readonly children: React.ReactNode;
}) {
  return (
    <div className={`grid content-start gap-xs ${className ?? ""}`}>
      <Label htmlFor={id}>{label}</Label>
      {children}
      {problem === undefined ? null : <p className="text-sm text-danger">{problem}</p>}
    </div>
  );
}

/** The answer of a trace: the results on the left, the units by fate on the right; stacked on a narrow screen. */
function TraceView({ report }: { readonly report: RecallTraceReport }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.search;
  const milliseconds = (value: number | null) => (value === null ? null : `${Math.round(value)} ms`);

  return (
    <div className="grid items-start gap-lg xl:grid-cols-[minmax(0,1fr)_minmax(0,1.4fr)]">
      <Card aria-labelledby="trace-results">
        <CardTitle id="trace-results">{text.resultsTitle(report.results.length)}</CardTitle>
        {report.results.length === 0 ? (
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

/** One fate's segment: its name with icon, how many units met it and their share, then the units in fused order. */
function SegmentBlock({ segment, report }: { readonly segment: Segment; readonly report: RecallTraceReport }) {
  const text = useDictionary().memory.search;
  const style = FATE_STYLES[segment.fate];
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
      {segment.units.length === 0 ? null : (
        <ul className="grid">
          {segment.units.map((unit) => (
            <UnitRow key={`${unit.fusedRank}`} unit={unit} />
          ))}
        </ul>
      )}
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
