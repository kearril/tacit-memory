import { cn } from "cn";
import { ChevronLeft, ChevronRight } from "lucide-react";
import { useEffect, useId, useMemo, useState } from "react";
import { useSearchParams } from "react-router";
import {
  type ActivityItemView,
  type ActivityMonthView,
  type ActivitySummaryView,
  useActivityMonth,
  useActivityMonths,
} from "../../api/memory";
import { QueryResult } from "../../components/query-result";
import { useDictionary, useSessionStatus } from "../../app/session-context";
import { StickyBar } from "../../components/ui/sticky-bar";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import { Select } from "../../components/ui/select";
import { entryBox } from "../../components/ui/entry-box";
import { BELOW_BARS } from "../../components/ui/sticky";
import { dayText } from "../../lib/format";
import { dayLevel, HEAT_LEVEL_CLASS } from "../../lib/heatmap";
import { CodePill, FilePath, FileProblems, QuoteList, QuoteToggle, WorkstreamPill } from "./parts";
import { monthDays, type TimelineEntry, timelineOf } from "./timeline";
import { activityParams, type ActivityQuery, activityQueryOf, dayAnchor } from "../../lib/routes";

/** Scrolls a day of the timeline to the top, below the sticky bars. */
function scrollToDay(date: string) {
  document.getElementById(dayAnchor(date))?.scrollIntoView({ block: "start" });
}

/**
 * The activity view of the memory page: one month at a time in a reading column. The month, the day to go to and the
 * workstream to keep are in the address, so the dashboard heatmap and the workstream briefs can link to them. A sticky
 * bar holds the month, the counts and the workstream above a strip of the month's days shaded as the heatmap shades
 * them; the month summary follows, then the timeline newest first: each day's date beside a rail with a dot for every
 * activity, and each week summary heading its week. The month's file closes it.
 */
export function MemoryActivityPage() {
  const dictionary = useDictionary();
  const text = dictionary.memory.activity;
  const months = useActivityMonths();

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-prompts) grid-cols-1 gap-md">
      <h1 className="sr-only">{dictionary.memory.views.activity}</h1>
      <QueryResult query={months} title={text.failed}>
        {(data) => <MonthView months={data.months} current={data.current} />}
      </QueryResult>
    </div>
  );
}

function MonthView({ months, current }: { readonly months: readonly string[]; readonly current: string }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.activity;
  const [search, setSearch] = useSearchParams();
  const query = activityQueryOf(search, current);
  const view = useActivityMonth(query.month);

  // Every month with a file, the current one, and the one in the address, newest first.
  const choices = useMemo(
    () => [...new Set([...months, current, query.month])].sort().reverse(),
    [months, current, query.month],
  );
  const update = (change: Partial<ActivityQuery>) =>
    setSearch(activityParams({ ...query, ...change }), { replace: true });

  const loaded = view.data !== undefined;
  useEffect(() => {
    if (loaded && query.day !== null) {
      scrollToDay(query.day);
    }
  }, [loaded, query.day]);

  const timeline = useMemo(
    () => (view.data === undefined ? [] : timelineOf(view.data, query.workstream)),
    [view.data, query.workstream],
  );
  const workstreams = useMemo(
    () =>
      [
        ...new Set([
          ...(view.data?.days.flatMap((day) => day.activities.flatMap((item) => item.workstream ?? [])) ?? []),
          ...(query.workstream === null ? [] : [query.workstream]),
        ]),
      ].sort(),
    [view.data, query.workstream],
  );

  return (
    <>
      <MonthBar
        choices={choices}
        month={query.month}
        view={view.data}
        timeline={timeline}
        onMonth={(month) => update({ month, day: null })}
        onPick={(date) => {
          update({ day: date });
          scrollToDay(date);
        }}
      >
        <Select
          aria-label={text.workstream}
          className="ml-auto w-auto max-w-full"
          value={query.workstream ?? ""}
          onValueChange={(value) => update({ workstream: value || null })}
          options={[
            { value: "", label: text.workstreamAll },
            ...workstreams.map((name) => ({ value: name, label: name })),
          ]}
        />
      </MonthBar>

      <QueryResult query={view} title={text.failed} missing={text.notFound}>
        {(data) => <Timeline view={data} timeline={timeline} workstream={query.workstream} />}
      </QueryResult>
    </>
  );
}

/**
 * The sticky bar: the month with the months before and after it, how many days and activities are shown, the workstream
 * (given as `children`), then a strip of the month's days, each shaded as the heatmap shades it; a day with activities
 * goes to them.
 */
function MonthBar({
  choices,
  month,
  view,
  timeline,
  onMonth,
  onPick,
  children,
}: {
  readonly choices: readonly string[];
  readonly month: string;
  readonly view: ActivityMonthView | undefined;
  readonly timeline: readonly TimelineEntry[];
  readonly onMonth: (month: string) => void;
  readonly onPick: (date: string) => void;
  readonly children: React.ReactNode;
}) {
  const text = useDictionary().memory.activity;
  const position = choices.indexOf(month);
  const days = timeline.flatMap((entry) => (entry.kind === "day" ? [entry] : []));
  const counts = new Map(days.map((day) => [day.date, day.activities.length]));
  return (
    <StickyBar className="grid gap-sm">
      <div className="flex items-center gap-sm">
        <div className="flex items-center gap-xs">
          <Button
            variant="ghost"
            size="icon"
            aria-label={text.previous}
            disabled={position >= choices.length - 1}
            onClick={() => onMonth(choices[position + 1]!)}
          >
            <ChevronLeft aria-hidden />
          </Button>
          <Select
            aria-label={text.month}
            className="w-auto"
            value={month}
            onValueChange={onMonth}
            options={choices.map((choice) => ({ value: choice, label: choice }))}
          />
          <Button
            variant="ghost"
            size="icon"
            aria-label={text.next}
            disabled={position <= 0}
            onClick={() => onMonth(choices[position - 1]!)}
          >
            <ChevronRight aria-hidden />
          </Button>
        </div>
        {view === undefined ? null : (
          <span className="hidden text-sm text-ink-muted tabular-nums sm:inline">
            {text.totals(
              days.length,
              days.reduce((sum, day) => sum + day.activities.length, 0),
            )}
          </span>
        )}
        {children}
      </div>
      {view === undefined ? null : (
        <div role="group" aria-label={text.calendar} className="flex gap-px">
          {monthDays(view.month, counts).map((day) => (
            <button
              key={day.date}
              type="button"
              disabled={day.count === 0}
              title={text.day(day.date, day.count)}
              aria-label={text.day(day.date, day.count)}
              onClick={() => onPick(day.date)}
              className={cn(
                "grid h-control min-w-0 flex-1 place-items-center rounded-xs text-xs tabular-nums outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
                HEAT_LEVEL_CLASS[dayLevel(day.count)],
                day.count === 0
                  ? "text-ink-muted"
                  : "cursor-pointer hover:ring-(length:--tk-border-thin) hover:ring-accent",
                dayLevel(day.count) >= 3 && "text-surface",
              )}
            >
              <span aria-hidden className="hidden md:inline">
                {Number(day.date.slice(8))}
              </span>
            </button>
          ))}
        </div>
      )}
    </StickyBar>
  );
}

/** The month summary above, then the days and week summaries in one card, then the month's file. */
function Timeline({
  view,
  timeline,
  workstream,
}: {
  readonly view: ActivityMonthView;
  readonly timeline: readonly TimelineEntry[];
  readonly workstream: string | null;
}) {
  const text = useDictionary().memory.activity;
  const hasDays = timeline.some((entry) => entry.kind === "day");
  return (
    <>
      {timeline.map((entry) =>
        entry.kind === "summary" && entry.summary.kind !== "week" ? (
          <Summary key={entry.summary.label} summary={entry.summary} />
        ) : null,
      )}
      <Card aria-label={text.timeline} className="gap-xl">
        {hasDays ? null : <p className="text-sm text-ink-muted">{workstream === null ? text.empty : text.noMatch}</p>}
        {timeline.map((entry) =>
          entry.kind === "day" ? (
            <DaySection key={entry.date} date={entry.date} activities={entry.activities} />
          ) : entry.summary.kind === "week" ? (
            <Summary key={entry.summary.label} summary={entry.summary} />
          ) : null,
        )}
      </Card>
      <footer className="grid gap-xs px-md text-xs text-ink-muted">
        <span>{text.sourceFile}</span>
        <FilePath path={view.file.path} />
        <FileProblems problems={view.file.problems} />
      </footer>
    </>
  );
}

/** A day: its date and weekday in a column beside a rail that carries a dot for each of its activities. */
function DaySection({ date, activities }: { readonly date: string; readonly activities: readonly ActivityItemView[] }) {
  const locale = useSessionStatus().language === "zh" ? "zh-CN" : "en";
  return (
    <section
      id={dayAnchor(date)}
      aria-labelledby={`${dayAnchor(date)}-title`}
      className={cn(BELOW_BARS, "grid grid-cols-[var(--tk-size-control)_minmax(0,1fr)] gap-x-md")}
    >
      <h2 id={`${dayAnchor(date)}-title`} className="grid content-start justify-items-center text-center">
        <span className="sr-only">{date}</span>
        <span aria-hidden className="text-xl leading-(--tk-line-height-tight) font-semibold tabular-nums">
          {Number(date.slice(8))}
        </span>
        <span className="text-xs font-regular text-ink-muted">{dayText(date, locale, { weekday: "short" })}</span>
      </h2>
      <ul className="grid gap-lg border-l-(length:--tk-border-thick) border-line pl-md">
        {activities.map((item) => (
          <ActivityRow key={item.code} item={item} />
        ))}
      </ul>
    </section>
  );
}

/**
 * An activity, marked by a dot on the rail. Above its text, its workstream (or a mark that it has none), the machine it
 * was written on when it names one, a button that opens the user's words when they were kept, and its short code, which
 * shows on hover or focus.
 */
function ActivityRow({ item }: { readonly item: ActivityItemView }) {
  const text = useDictionary().memory.activity;
  const quotesId = useId();
  const [open, setOpen] = useState(false);
  const { lines, sources } = item;
  return (
    <li className="group relative grid gap-xs">
      <span
        aria-hidden
        className="absolute top-[calc(var(--tk-font-size-xl)/2-var(--tk-space-sm)/2)] -left-[calc(var(--tk-space-md)+var(--tk-border-thick)/2+var(--tk-space-sm)/2)] size-(--tk-space-sm) rounded-full bg-accent ring-(length:--tk-border-thick) ring-surface transition-transform group-hover:scale-125"
      />
      <div className="flex min-h-(--tk-font-size-xl) flex-wrap items-center gap-sm text-xs text-ink-muted">
        {item.workstream === null ? (
          <span className="rounded-full border-(length:--tk-border-thin) border-dashed border-ink-muted/40 px-sm">
            {text.noWorkstream}
          </span>
        ) : (
          <WorkstreamPill name={item.workstream} />
        )}
        {item.machine === null ? null : (
          <span title={text.machine(item.machine)} aria-label={text.machine(item.machine)}>
            {item.machine}
          </span>
        )}
        {sources.some((source) => source.quote !== null) ? (
          <QuoteToggle open={open} controls={quotesId} onToggle={() => setOpen(!open)} />
        ) : null}
        <CodePill code={item.code} reveal className="ml-auto" />
      </div>
      <p className="leading-(--tk-line-height-normal)">{item.text}</p>
      {lines.map((line, index) => (
        <p key={index} className="text-sm leading-(--tk-line-height-normal) text-ink-muted">
          {line}
        </p>
      ))}
      {open ? <QuoteList id={quotesId} sources={sources} /> : null}
    </li>
  );
}

/** A summary: the month's on the accent above the timeline, a week's in a dashed box heading its week. */
function Summary({ summary }: { readonly summary: ActivitySummaryView }) {
  const text = useDictionary().memory.activity;
  const week = summary.kind === "week";
  const title = week ? text.weekSummary(summary.label) : text.monthSummary(summary.label);
  return (
    <aside
      aria-label={title}
      className={cn("grid gap-xs", week ? entryBox({ tone: "accent" }) : "rounded-md bg-accent-soft px-md py-sm")}
    >
      <p className="flex flex-wrap items-baseline gap-x-sm text-sm font-semibold">
        <span className="text-accent">{title}</span>
        <span className="text-xs font-regular text-ink-muted tabular-nums">{`${summary.first} – ${summary.last}`}</span>
      </p>
      <p className="text-sm leading-(--tk-line-height-normal)">{summary.text}</p>
    </aside>
  );
}
