import { cn } from "cn";
import { ChevronLeft, ChevronRight, Quote } from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { useSearchParams } from "react-router";
import { ApiError } from "../../api/client";
import {
  type ActivityItemView,
  type ActivityMonthView,
  type ActivitySummaryView,
  useActivityMonth,
  useActivityMonths,
} from "../../api/memory";
import { useDictionary, useSessionStatus } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import { Label } from "../../components/ui/label";
import { errorTextOf } from "../../i18n/messages";
import { dayText } from "../dashboard/format";
import { dayLevel, type HeatLevel } from "../dashboard/heatmap";
import { CodePill, FilePath, SourceNote, WorkstreamPill } from "./parts";
import { splitSources } from "./sources";
import {
  type ActivityQuery,
  activityParams,
  activityQueryOf,
  dayAnchor,
  monthGrid,
  type TimelineEntry,
  timelineOf,
} from "./timeline";

/** A native select in the look of the inputs. */
const SELECT_CLASS =
  "h-control w-full min-w-0 rounded-md border-(length:--tk-border-thin) border-line bg-surface-raised px-md text-base text-ink outline-none focus-visible:border-accent focus-visible:bg-surface focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent";

const levelClass: Record<HeatLevel, string> = {
  0: "bg-heat-0",
  1: "bg-heat-1",
  2: "bg-heat-2",
  3: "bg-heat-3",
  4: "bg-heat-4",
};

/** Scrolls a day of the timeline to the top, below the sticky bars. */
function scrollToDay(date: string) {
  document.getElementById(dayAnchor(date))?.scrollIntoView({ block: "start" });
}

/**
 * The activity view of the memory page: one month at a time in a reading column. The month, the day to go to and the
 * workstream to keep are in the address, so the dashboard heatmap and the workstream briefs can link to them. A card on
 * top chooses the month and the workstream and holds the month's mini calendar; the timeline below lists the days oldest
 * first with their dates sticking to the top, the month summary first and each week summary after its week.
 */
export function MemoryActivityPage() {
  const dictionary = useDictionary();
  const text = dictionary.memory.activity;
  const months = useActivityMonths();

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-prompts) gap-lg">
      <h1 className="sr-only">{dictionary.memory.views.activity}</h1>
      {months.isError ? (
        <Alert tone="danger">
          <AlertTitle>{text.failed}</AlertTitle>
          <AlertDescription>{errorTextOf(months.error, dictionary)}</AlertDescription>
        </Alert>
      ) : months.data === undefined ? (
        <div aria-busy="true" />
      ) : (
        <MonthView months={months.data.months} current={months.data.current} />
      )}
    </div>
  );
}

function MonthView({ months, current }: { readonly months: readonly string[]; readonly current: string }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.activity;
  const [search, setSearch] = useSearchParams();
  const query = activityQueryOf(search, current);
  const view = useActivityMonth(query.month);
  const missing = view.error instanceof ApiError && view.error.status === 404;

  // Every month with a file, the current one, and the one in the address, newest first.
  const choices = useMemo(
    () => [...new Set([...months, current, query.month])].sort().reverse(),
    [months, current, query.month],
  );
  const position = choices.indexOf(query.month);
  const update = (change: Partial<ActivityQuery>) =>
    setSearch(activityParams({ ...query, ...change }), { replace: true });

  const loaded = view.data !== undefined;
  useEffect(() => {
    if (loaded && query.day !== null) {
      scrollToDay(query.day);
    }
  }, [loaded, query.day]);

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
      <Card>
        <div className="grid gap-md md:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
          <div className="grid content-start gap-xs">
            <Label htmlFor="activity-month">{text.month}</Label>
            <div className="flex items-center gap-xs">
              <Button
                variant="ghost"
                size="icon"
                aria-label={text.previous}
                disabled={position >= choices.length - 1}
                onClick={() => update({ month: choices[position + 1], day: null })}
              >
                <ChevronLeft aria-hidden />
              </Button>
              <select
                id="activity-month"
                className={SELECT_CLASS}
                value={query.month}
                onChange={(event) => update({ month: event.target.value, day: null })}
              >
                {choices.map((month) => (
                  <option key={month} value={month}>
                    {month}
                  </option>
                ))}
              </select>
              <Button
                variant="ghost"
                size="icon"
                aria-label={text.next}
                disabled={position <= 0}
                onClick={() => update({ month: choices[position - 1], day: null })}
              >
                <ChevronRight aria-hidden />
              </Button>
            </div>
          </div>
          <div className="grid content-start gap-xs">
            <Label htmlFor="activity-workstream">{text.workstream}</Label>
            <select
              id="activity-workstream"
              className={SELECT_CLASS}
              value={query.workstream ?? ""}
              onChange={(event) => update({ workstream: event.target.value || null })}
            >
              <option value="">{text.workstreamAll}</option>
              {workstreams.map((name) => (
                <option key={name} value={name}>
                  {name}
                </option>
              ))}
            </select>
          </div>
        </div>
        {view.data === undefined ? null : (
          <>
            <FilePath path={view.data.file.path} />
            <Problems view={view.data} />
            <MiniCalendar
              view={view.data}
              workstream={query.workstream}
              onPick={(date) => {
                update({ day: date });
                scrollToDay(date);
              }}
            />
          </>
        )}
      </Card>

      {view.isError ? (
        <Alert tone={missing ? "warning" : "danger"}>
          <AlertTitle>{missing ? text.notFound : text.failed}</AlertTitle>
          <AlertDescription>{errorTextOf(view.error, dictionary)}</AlertDescription>
        </Alert>
      ) : view.data === undefined ? (
        <div aria-busy="true" />
      ) : (
        <Timeline view={view.data} workstream={query.workstream} />
      )}
    </>
  );
}

/** The month file's problems, in the report's words. */
function Problems({ view }: { readonly view: ActivityMonthView }) {
  const text = useDictionary().memory.activity;
  const problems = view.file.problems;
  if (problems.length === 0) {
    return null;
  }
  return (
    <Alert tone="warning">
      <AlertTitle>{text.problemsTitle(problems.length)}</AlertTitle>
      <AlertDescription>
        <ul className="grid gap-xs">
          {problems.map((problem) => (
            <li key={`${problem.location}-${problem.kind}`}>
              {`${problem.location} · ${problem.kind}: ${problem.description}`}
            </li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  );
}

/** The month as a calendar from Monday, each day shaded as the heatmap shades it; a day with activities goes to them. */
function MiniCalendar({
  view,
  workstream,
  onPick,
}: {
  readonly view: ActivityMonthView;
  readonly workstream: string | null;
  readonly onPick: (date: string) => void;
}) {
  const text = useDictionary().memory.activity;
  const counts = useMemo(
    () =>
      new Map(
        view.days.map((day) => [
          day.date,
          day.activities.filter((item) => workstream === null || item.workstream === workstream).length,
        ]),
      ),
    [view.days, workstream],
  );
  const grid = monthGrid(view.month, counts);
  return (
    <div role="group" aria-label={text.calendar} className="grid w-fit grid-cols-7 gap-xs">
      {text.weekdays.map((weekday) => (
        <span key={weekday} aria-hidden className="text-center text-xs text-ink-muted">
          {weekday}
        </span>
      ))}
      {grid.flat().map((day, index) =>
        day === null ? (
          <span key={`empty-${index}`} />
        ) : (
          <button
            key={day.date}
            type="button"
            disabled={day.count === 0}
            title={text.day(day.date, day.count)}
            aria-label={text.day(day.date, day.count)}
            onClick={() => onPick(day.date)}
            className={cn(
              "grid size-(--tk-size-control) place-items-center rounded-xs text-xs tabular-nums outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
              levelClass[dayLevel(day.count)],
              day.count === 0
                ? "text-ink-muted"
                : "cursor-pointer hover:ring-(length:--tk-border-thin) hover:ring-ink-muted",
              dayLevel(day.count) >= 3 && "text-surface",
            )}
          >
            {Number(day.date.slice(8))}
          </button>
        ),
      )}
    </div>
  );
}

/** The timeline in one card: the days with their sticky dates, and the summaries between them. */
function Timeline({ view, workstream }: { readonly view: ActivityMonthView; readonly workstream: string | null }) {
  const text = useDictionary().memory.activity;
  const timeline = useMemo(() => timelineOf(view, workstream), [view, workstream]);
  const hasDays = timeline.some((entry) => entry.kind === "day");
  return (
    <Card aria-label={text.timeline} className="gap-lg">
      {hasDays ? null : <p className="text-sm text-ink-muted">{workstream === null ? text.empty : text.noMatch}</p>}
      {timeline.map((entry) => (
        <TimelineItem key={entry.kind === "day" ? entry.date : entry.summary.label} entry={entry} />
      ))}
    </Card>
  );
}

function TimelineItem({ entry }: { readonly entry: TimelineEntry }) {
  return entry.kind === "day" ? (
    <DaySection date={entry.date} activities={entry.activities} />
  ) : (
    <SummaryBar summary={entry.summary} />
  );
}

/** A day: its date and count sticking to the top while its activities scroll by. */
function DaySection({ date, activities }: { readonly date: string; readonly activities: readonly ActivityItemView[] }) {
  const text = useDictionary().memory.activity;
  const locale = useSessionStatus().language === "zh" ? "zh-CN" : "en";
  return (
    <section
      id={dayAnchor(date)}
      aria-label={date}
      className="grid scroll-mt-(--tk-size-nav-bar) gap-sm lg:scroll-mt-0"
    >
      <h2 className="sticky top-(--tk-size-nav-bar) z-(--tk-layer-base) flex items-baseline gap-sm border-b-(length:--tk-border-thin) border-line bg-surface py-xs text-sm font-semibold lg:top-0">
        <span className="tabular-nums">{date}</span>
        <span className="font-regular text-ink-muted">{dayText(date, locale, { weekday: "short" })}</span>
        <span className="text-xs font-regular text-ink-muted">{text.count(activities.length)}</span>
      </h2>
      <ul className="grid gap-md">
        {activities.map((item) => (
          <ActivityRow key={item.code} item={item} />
        ))}
      </ul>
    </section>
  );
}

/** An activity: its workstream, its text and its short code; the user's words are folded behind a quote button. */
function ActivityRow({ item }: { readonly item: ActivityItemView }) {
  const text = useDictionary().memory.activity;
  const [open, setOpen] = useState(false);
  const { lines, sources } = splitSources(item.continuation);
  return (
    <li className="grid grid-cols-[minmax(0,1fr)_auto] items-start gap-x-md gap-y-xs">
      <div className="grid min-w-0 gap-xs">
        <p className="leading-(--tk-line-height-normal)">
          {item.workstream === null ? null : (
            <span className="mr-sm inline-flex align-middle">
              <WorkstreamPill name={item.workstream} />
            </span>
          )}
          {item.text}
        </p>
        {lines.map((line, index) => (
          <p key={index} className="text-sm leading-(--tk-line-height-normal) text-ink-muted">
            {line.trim()}
          </p>
        ))}
        {open ? sources.map((source, index) => <SourceNote key={index} source={source} />) : null}
      </div>
      <div className="flex items-center gap-xs">
        {sources.length === 0 ? null : (
          <button
            type="button"
            aria-expanded={open}
            aria-label={open ? text.hideQuote : text.showQuote}
            title={open ? text.hideQuote : text.showQuote}
            onClick={() => setOpen(!open)}
            className={cn(
              "grid cursor-pointer place-items-center rounded-sm p-xs outline-none hover:bg-surface-raised hover:text-ink focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
              open ? "text-accent" : "text-ink-muted",
            )}
          >
            <Quote aria-hidden className="size-(--tk-font-size-sm)" />
          </button>
        )}
        <CodePill code={item.code} />
      </div>
    </li>
  );
}

/** A summary as a bar between the days: the month's at the start, a week's after the week. */
function SummaryBar({ summary }: { readonly summary: ActivitySummaryView }) {
  const text = useDictionary().memory.activity;
  const week = summary.kind === "week";
  const title = week ? text.weekSummary(summary.label) : text.monthSummary(summary.label);
  return (
    <aside
      aria-label={title}
      className={cn("grid gap-xs rounded-md px-md py-sm", week ? "bg-surface-raised" : "bg-accent-soft")}
    >
      <p className="flex flex-wrap items-baseline gap-x-sm text-sm font-semibold">
        {title}
        <span className="text-xs font-regular text-ink-muted tabular-nums">{`${summary.first} – ${summary.last}`}</span>
      </p>
      <p className="text-sm leading-(--tk-line-height-normal)">{summary.text}</p>
    </aside>
  );
}
