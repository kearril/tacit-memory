import { Star } from "lucide-react";
import { type KeyboardEvent, type ReactNode, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router";
import { useActivity } from "../../api/activity";
import { useDictionary, useSessionStatus } from "../../app/session-context";
import { Alert, AlertDescription } from "../../components/ui/alert";
import { Badge } from "../../components/ui/badge";
import { Card, CardDescription, CardTitle } from "../../components/ui/card";
import { errorTextOf } from "../../i18n/messages";
import { activityParams } from "../memory/timeline";
import { memoryPathOf } from "../memory/views";
import { dayText } from "./format";
import { calendarColumns, dayLevel, fillWindow, type HeatDay, type HeatLevel, monthLabels, statsOf } from "./heatmap";

const levelClass: Record<HeatLevel, string> = {
  0: "bg-heat-0",
  1: "bg-heat-1",
  2: "bg-heat-2",
  3: "bg-heat-3",
  4: "bg-heat-4",
};

/** A `yyyy-MM-dd` date as the interface shows it: month, day and weekday, in the memory language. */
function dateText(date: string, locale: string): string {
  return dayText(date, locale, { month: "long", day: "numeric", weekday: "short" });
}

/**
 * The memory activity card, in three compact rows: the title with the four numbers of the window; a calendar of the last
 * year in the manner of GitHub's contribution graph, one column per week and one cell per day across the whole width,
 * shaded by how many activities the day has, with the consolidation days marked; and one line with the activities of the
 * day in view beside the legend. Hovering or focusing a cell puts that day in view; the arrow keys move between days.
 * Clicking a day with activities opens it in the memory page's activity view.
 */
export function HeatmapCard({ className }: { readonly className: string }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.activity;
  const activity = useActivity();

  const title = (
    <div className="flex items-baseline gap-sm">
      <CardTitle id="activity-title">{dictionary.dashboard.cards.activity}</CardTitle>
      <CardDescription>{text.window}</CardDescription>
    </div>
  );

  return (
    <Card aria-labelledby="activity-title" className={`py-md ${className}`}>
      {activity.data === undefined ? (
        <>
          <header>{title}</header>
          {activity.isError ? (
            <Alert tone="danger">
              <AlertDescription>{errorTextOf(activity.error, dictionary)}</AlertDescription>
            </Alert>
          ) : (
            <div aria-busy="true" className="py-xl" />
          )}
        </>
      ) : (
        <Heatmap report={activity.data} title={title} />
      )}
    </Card>
  );
}

function Heatmap({
  report,
  title,
}: {
  readonly report: NonNullable<ReturnType<typeof useActivity>["data"]>;
  readonly title: ReactNode;
}) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.activity;
  const locale = useSessionStatus().language === "zh" ? "zh-CN" : "en";

  const days = useMemo(() => fillWindow(report), [report]);
  const columns = useMemo(() => calendarColumns(days), [days]);
  const months = useMemo(() => monthLabels(columns), [columns]);
  const stats = useMemo(() => statsOf(days), [days]);

  const navigate = useNavigate();
  const [selected, setSelected] = useState(days.length - 1);
  const [hovered, setHovered] = useState<number | null>(null);
  const cells = useRef(new Map<number, HTMLButtonElement>());
  const shown = days[hovered ?? Math.min(selected, days.length - 1)];

  // Up and down move by a day, left and right by a week; Home and End go to the first and the last day.
  const onKeyDown = (event: KeyboardEvent) => {
    const step: Record<string, number> = { ArrowUp: -1, ArrowDown: 1, ArrowLeft: -7, ArrowRight: 7 };
    let next: number | null = null;
    if (event.key in step) {
      next = selected + step[event.key];
    } else if (event.key === "Home") {
      next = 0;
    } else if (event.key === "End") {
      next = days.length - 1;
    }
    if (next !== null && next >= 0 && next < days.length) {
      event.preventDefault();
      cells.current.get(next)?.focus();
    }
  };

  return (
    <>
      <header className="flex flex-wrap items-center justify-between gap-x-xl gap-y-sm">
        {title}
        <dl className="flex flex-wrap gap-x-lg gap-y-xs">
          <Stat label={text.total} value={String(stats.total)} />
          <Stat label={text.activeDays} value={text.days(stats.activeDays)} />
          <Stat label={text.longestStreak} value={text.days(stats.longestStreak)} />
          <Stat
            label={text.topWorkstream}
            value={stats.topWorkstream?.workstream ?? text.noWorkstream}
            note={stats.topWorkstream === null ? undefined : text.activities(stats.topWorkstream.count)}
          />
        </dl>
      </header>

      {/* The ring of a selected or hovered cell is drawn outside the cell; the padding keeps the scroll box from cutting it. */}
      <div className="overflow-x-auto p-(--tk-border-thick) pb-xs">
        <div
          role="group"
          aria-label={dictionary.dashboard.cards.activity}
          onKeyDown={onKeyDown}
          onMouseLeave={() => setHovered(null)}
          className="grid gap-xs"
          style={{
            gridTemplateColumns: `auto repeat(${columns.length}, minmax(0, 1fr))`,
            minWidth: `calc(${columns.length} * var(--tk-size-heat-cell))`,
          }}
        >
          {months.map((label) => (
            <span
              key={label.date}
              aria-hidden
              className="text-xs whitespace-nowrap text-ink-muted"
              style={{ gridColumn: label.column + 2, gridRow: 1 }}
            >
              {dayText(label.date, locale, { month: "short" })}
            </span>
          ))}
          {text.weekdays.map((weekday, row) =>
            row % 2 === 0 ? (
              <span
                key={weekday}
                aria-hidden
                className="self-center pr-xs text-xs leading-(--tk-line-height-tight) text-ink-muted"
                style={{ gridColumn: 1, gridRow: row + 2 }}
              >
                {weekday}
              </span>
            ) : null,
          )}
          {columns.map((week, column) =>
            week.map((day, row) => {
              if (day === null) {
                return null;
              }
              const index = column * 7 + row;
              const isSelected = index === selected;
              return (
                <button
                  key={day.date}
                  ref={(element) => {
                    if (element === null) {
                      cells.current.delete(index);
                    } else {
                      cells.current.set(index, element);
                    }
                  }}
                  type="button"
                  tabIndex={isSelected ? 0 : -1}
                  aria-pressed={isSelected}
                  aria-label={text.cell(dateText(day.date, locale), day.total, day.consolidated)}
                  onClick={() =>
                    day.total === 0
                      ? setSelected(index)
                      : navigate(
                          `${memoryPathOf("activity")}?${activityParams({ month: day.date.slice(0, 7), day: day.date, workstream: null })}`,
                        )
                  }
                  onFocus={() => setSelected(index)}
                  onMouseEnter={() => setHovered(index)}
                  style={{ gridColumn: column + 2, gridRow: row + 2 }}
                  className={`grid aspect-square w-full cursor-pointer place-items-center rounded-xs transition-shadow outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
                    levelClass[dayLevel(day.total)]
                  } ${isSelected ? "ring-(length:--tk-border-thick) ring-ink" : "hover:ring-(length:--tk-border-thin) hover:ring-ink-muted"}`}
                >
                  {day.consolidated ? <ConsolidationStar /> : null}
                </button>
              );
            }),
          )}
        </div>
      </div>

      <div className="flex flex-wrap items-center justify-between gap-x-lg">
        <DayDetail day={shown} locale={locale} />
        <Legend />
      </div>

      {report.skippedFiles.length === 0 ? null : (
        <Alert tone="warning">
          <AlertDescription>
            {text.skipped(report.skippedFiles.join(locale === "zh-CN" ? "、" : ", "))}
          </AlertDescription>
        </Alert>
      )}
      {report.historyUnreadable ? (
        <Alert tone="info">
          <AlertDescription>{text.historyUnreadable}</AlertDescription>
        </Alert>
      ) : null}
    </>
  );
}

/**
 * The activities of the day in view, on one line of fixed height so that moving over the cells never moves the card: the
 * date, the total, the consolidation mark, and the count per workstream with the unlabeled ones last.
 */
function DayDetail({ day, locale }: { readonly day: HeatDay; readonly locale: string }) {
  const text = useDictionary().dashboard.activity;
  const rows = [
    ...day.workstreams.map((item) => ({ name: item.workstream, count: item.count, muted: false })),
    ...(day.unlabeled > 0 ? [{ name: text.unlabeled, count: day.unlabeled, muted: true }] : []),
  ];
  return (
    <div className="flex h-control min-w-0 flex-1 items-center gap-md overflow-hidden text-sm whitespace-nowrap">
      <span className="font-medium">{dateText(day.date, locale)}</span>
      <span className="text-ink-muted">{day.total === 0 ? text.none : text.activities(day.total)}</span>
      {day.consolidated ? (
        <Badge tone="success">
          <Star aria-hidden fill="currentColor" strokeWidth={0} />
          {text.consolidated}
        </Badge>
      ) : null}
      <span className="flex min-w-0 items-center gap-md overflow-hidden">
        {rows.map((row) => (
          <span key={row.name} className="flex items-center gap-xs">
            <span
              aria-hidden
              className={`size-(--tk-space-sm) shrink-0 rounded-full ${row.muted ? "bg-heat-1" : "bg-heat-3"}`}
            />
            <span className={row.muted ? "text-ink-muted" : ""}>{row.name}</span>
            <span className="font-medium tabular-nums">{row.count}</span>
          </span>
        ))}
      </span>
    </div>
  );
}

/** One number of the window, inline: its label, then its value. */
function Stat({ label, value, note }: { readonly label: string; readonly value: string; readonly note?: string }) {
  return (
    <div className="flex items-baseline gap-xs whitespace-nowrap">
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd className="text-sm font-semibold">
        {value}
        {note === undefined ? null : <span className="ml-xs text-xs font-regular text-ink-muted">{note}</span>}
      </dd>
    </div>
  );
}

/** The legend: the five shades from less to more, and the mark of a consolidation day. */
function Legend() {
  const text = useDictionary().dashboard.activity;
  return (
    <div aria-hidden className="flex items-center gap-md text-xs text-ink-muted">
      <span className="flex items-center gap-xs">
        {text.less}
        {([0, 1, 2, 3, 4] as const).map((level) => (
          <span key={level} className={`size-(--tk-size-heat-cell) rounded-xs ${levelClass[level]}`} />
        ))}
        {text.more}
      </span>
      <span className="flex items-center gap-xs">
        <span className="grid size-(--tk-size-heat-cell) place-items-center rounded-xs bg-heat-0">
          <ConsolidationStar />
        </span>
        {text.consolidated}
      </span>
    </div>
  );
}

/** The mark of a consolidation day inside a cell: a small filled star in the success colour. */
function ConsolidationStar() {
  return <Star aria-hidden fill="currentColor" strokeWidth={0} className="size-3/4 text-success" />;
}
