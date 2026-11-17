import { useQueryClient } from "@tanstack/react-query";
import { ArrowDown, CalendarDays, Check, ChevronDown, Pause, Radio, ScrollText, Search, X } from "lucide-react";
import { useCallback, useEffect, useLayoutEffect, useRef, useState } from "react";
import { Link, Navigate, useLocation, useNavigate, useParams, useSearchParams } from "react-router";
import type { ServiceLogDays, ServiceLogEntry, ServiceLogLevel } from "../../api/logs";
import { LOGS_QUERY_KEY, useLogDay, useLogDays } from "../../api/logs";
import { useDictionary, useSessionStatus } from "../../app/session-context";
import { Button } from "../../components/ui/button";
import { QueryFailed, QueryResult } from "../../components/query-result";
import { queryStateOf } from "../../components/query-state";
import { Card } from "../../components/ui/card";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "../../components/ui/dropdown-menu";
import { Input } from "../../components/ui/input";
import { IconTile } from "../../components/ui/icon-tile";
import { dayText } from "../../lib/format";
import { clockOf, dateOptions } from "./day";
import { applyFilters, highlightParts, levelCounts } from "./filter";
import { isAtBottom, unseenOf } from "./follow";
import { useFollowedLog } from "./use-followed-log";
import {
  filtersOf,
  isCalendarDate,
  LEVELS,
  logDateOf,
  type LogFilters,
  logPathOf,
  LOGS_PATH,
  searchOf,
} from "../../lib/routes";

/** The colour of each level's code; a warning or an error also tints its whole row. */
const LEVEL_STYLES: Readonly<
  Record<ServiceLogLevel, { readonly code: string; readonly row: string; readonly on: string }>
> = {
  information: { code: "text-ink-muted", row: "", on: "bg-surface-raised text-ink" },
  warning: { code: "text-warning", row: "bg-warning-soft", on: "bg-warning-soft text-warning" },
  error: { code: "text-danger", row: "bg-danger-soft", on: "bg-danger-soft text-danger" },
};

/**
 * The logs page: one day of the service log, oldest event first, today unless the address names another day. The events
 * scroll in an area of their own inside the card, which keeps its height whatever the day holds. The filters live in the
 * address, so a link can carry them and a reload keeps them; changing them replaces the address rather than adding to the
 * history. The page heading is for screen readers only, as the sidebar already shows which page this is.
 */
export function LogsPage() {
  const dictionary = useDictionary();
  const params = useParams();
  const param = params.date;
  const [search, setSearch] = useSearchParams();
  const filters = filtersOf(search);
  // At once rather than as a transition: the keyword box shows the address, and a key pressed before a transitioned
  // address arrived would be typed into the old value and lose the ones before it.
  const setFilters = (next: LogFilters) =>
    setSearch(new URLSearchParams(searchOf(next)), { replace: true, flushSync: true });
  const days = useLogDays();
  const [following, setFollowing] = useState(true);
  const named = logDateOf(params);
  const today = days.data?.today;
  if (param !== undefined && named !== undefined && named === today) {
    return <Navigate to={{ pathname: LOGS_PATH, search: searchOf(filters) }} replace />;
  }
  const date = param === undefined ? today : named;

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-page) gap-xl">
      <h1 className="sr-only">{dictionary.nav.logs}</h1>
      <QueryResult
        query={days}
        title={dictionary.logs.loadFailed}
        skeleton={<div aria-busy="true" className="min-h-dvh" />}
      >
        {(data) => (
          <Card>
            <LogHeader
              days={data}
              date={date ?? param ?? data.today}
              following={following}
              onFollowing={setFollowing}
            />
            {date === undefined ? (
              <NoLog today={data.today} date={param ?? ""} />
            ) : (
              <LogDay
                key={date}
                date={date}
                today={data.today}
                following={following && date === data.today}
                filters={filters}
                onFilters={setFilters}
              />
            )}
          </Card>
        )}
      </QueryResult>
    </div>
  );
}

/**
 * The head of the card: the page's icon, the day shown, the switch that follows today's log (only on today: a past day's
 * file no longer changes), and the button that picks another day.
 */
function LogHeader({
  days,
  date,
  following,
  onFollowing,
}: {
  readonly days: ServiceLogDays;
  readonly date: string;
  readonly following: boolean;
  readonly onFollowing: (following: boolean) => void;
}) {
  const dictionary = useDictionary();
  const text = dictionary.logs;
  const locale = useSessionStatus().language === "zh" ? "zh-CN" : "en";
  const navigate = useNavigate();
  const { search } = useLocation();
  // An address that names no calendar day is shown as written.
  const label = (day: string) =>
    !isCalendarDate(day)
      ? day
      : day === days.today
        ? `${text.today} · ${dayLabel(day, locale)}`
        : dayLabel(day, locale);

  return (
    <header className="grid grid-cols-[auto_minmax(0,1fr)_auto_auto] items-center gap-sm">
      <IconTile icon={ScrollText} />
      <h2 className="truncate text-lg font-semibold">{label(date)}</h2>
      {date === days.today ? (
        <Button
          type="button"
          variant="outline"
          aria-pressed={following}
          aria-label={text.follow}
          onClick={() => onFollowing(!following)}
          className={following ? "border-accent text-accent" : ""}
        >
          {following ? <Radio aria-hidden className="motion-safe:animate-pulse" /> : <Pause aria-hidden />}
          <span className="hidden sm:inline">{text.follow}</span>
        </Button>
      ) : (
        <span />
      )}
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <Button type="button" variant="outline" aria-label={text.dateLabel}>
            <CalendarDays aria-hidden />
            <span className="hidden sm:inline">{text.dateLabel}</span>
            <ChevronDown aria-hidden />
          </Button>
        </DropdownMenuTrigger>
        <DropdownMenuContent>
          {dateOptions(days).map((day) => (
            <DropdownMenuItem
              key={day}
              onSelect={() => void navigate({ pathname: logPathOf(day, days.today), search })}
            >
              {day === date ? <Check aria-hidden /> : <span aria-hidden className="size-(--tk-font-size-sm)" />}
              {label(day)}
            </DropdownMenuItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>
    </header>
  );
}

/**
 * One day's events under the filters, or why there are none. While it follows today's log, new events are added at the
 * end. A reader at the end of the scrolling area is kept there; one who has scrolled up stays put, and a button at the
 * bottom of the area counts the new events the filters let through and takes the reader back down.
 */
function LogDay({
  date,
  today,
  following,
  filters,
  onFilters,
}: {
  readonly date: string;
  readonly today: string;
  readonly following: boolean;
  readonly filters: LogFilters;
  readonly onFilters: (filters: LogFilters) => void;
}) {
  const dictionary = useDictionary();
  const queryClient = useQueryClient();
  const day = useLogDay(date);
  // The scrolling area of the events; null while it is not shown (no events, or none under the filters).
  const [view, setView] = useState<HTMLDivElement | null>(null);
  const [atBottom, setAtBottom] = useState(true);
  const [unseen, setUnseen] = useState(0);
  const atBottomRef = useRef(true);
  const filtersRef = useRef(filters);
  useEffect(() => {
    filtersRef.current = filters;
  });

  const onFresh = useCallback(
    (fresh: readonly ServiceLogEntry[]) => {
      if (following && !atBottomRef.current) {
        setUnseen((count) => count + unseenOf(fresh, filtersRef.current));
      }
    },
    [following],
  );
  // The service's today has moved on: reading the days again moves the page, which shows today, to the new day.
  const onDayTurned = useCallback(
    () => void queryClient.invalidateQueries({ queryKey: [...LOGS_QUERY_KEY, "days"] }),
    [queryClient],
  );
  const { held, offline } = useFollowedLog(day.data, following, onFresh, onDayTurned);
  const count = held?.entries.length ?? 0;

  useEffect(() => {
    if (view === null) {
      return;
    }
    const onScroll = () => {
      const bottom = isAtBottom(view);
      atBottomRef.current = bottom;
      setAtBottom(bottom);
      if (bottom) {
        setUnseen(0);
      }
    };
    view.addEventListener("scroll", onScroll, { passive: true });
    return () => view.removeEventListener("scroll", onScroll);
  }, [view]);

  // Followed, the area opens at the newest event, and stays there as events come while the reader is at the end.
  useLayoutEffect(() => {
    if (view !== null && following && count > 0 && atBottomRef.current) {
      scrollToEnd(view, "instant");
    }
  }, [view, following, count]);

  // The day's events come from following, not from the query, so only the query's failure is taken from it.
  const read = queryStateOf(day);
  if (read.kind === "missing") {
    return <NoLog today={today} date={date} />;
  }
  if (read.kind === "failed") {
    return (
      <QueryFailed
        title={dictionary.logs.loadFailed}
        error={read.error}
        retry={{ busy: day.isFetching, run: () => void day.refetch() }}
      />
    );
  }
  if (held === undefined) {
    return <div aria-busy="true" className="min-h-dvh" />;
  }
  const offlineLine = offline ? (
    <p role="status" className="text-sm text-warning">
      {dictionary.logs.offline}
    </p>
  ) : null;
  if (held.entries.length === 0) {
    return (
      <>
        {offlineLine}
        <p className="text-sm text-ink-muted">
          {date === today ? dictionary.logs.emptyToday : dictionary.logs.emptyDay}
        </p>
      </>
    );
  }
  const shown = applyFilters(held.entries, filters);
  return (
    <>
      <FilterBar entries={held.entries} shown={shown.length} filters={filters} onFilters={onFilters} />
      {offlineLine}
      {shown.length === 0 ? (
        <div className="grid justify-items-start gap-sm">
          <p className="text-sm text-ink-muted">{dictionary.logs.noMatch}</p>
          <Button type="button" variant="outline" onClick={() => onFilters({ levels: new Set(LEVELS), query: "" })}>
            <X aria-hidden />
            {dictionary.logs.clearFilters}
          </Button>
        </div>
      ) : (
        <div className="relative">
          <div
            ref={setView}
            tabIndex={0}
            aria-label={dictionary.logs.entriesLabel}
            className="max-h-(--tk-size-log-view) overflow-y-auto rounded-sm outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
          >
            <EntryList entries={shown} query={filters.query} />
          </div>
          {view !== null && following && unseen > 0 && !atBottom ? (
            <Button
              type="button"
              onClick={() => scrollToEnd(view, "smooth")}
              className="absolute bottom-md left-1/2 -translate-x-1/2 shadow-raised"
            >
              <ArrowDown aria-hidden />
              {dictionary.logs.unseen(unseen)}
            </Button>
          ) : null}
        </div>
      )}
    </>
  );
}

/** Scrolls an area to its end; smoothly when asked, unless the system asks for reduced motion. */
function scrollToEnd(area: HTMLElement, behavior: "instant" | "smooth") {
  const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
  area.scrollTo({ top: area.scrollHeight, behavior: reduced ? "instant" : behavior });
}

/**
 * The filters: a switch for each level with how many events of the day it holds, the keyword, and how many events are
 * shown of all. The last level switched on cannot be switched off.
 */
function FilterBar({
  entries,
  shown,
  filters,
  onFilters,
}: {
  readonly entries: readonly ServiceLogEntry[];
  readonly shown: number;
  readonly filters: LogFilters;
  readonly onFilters: (filters: LogFilters) => void;
}) {
  const text = useDictionary().logs;
  const counts = levelCounts(entries);
  const toggle = (level: ServiceLogLevel) => {
    const levels = new Set(filters.levels);
    if (levels.has(level)) {
      levels.delete(level);
    } else {
      levels.add(level);
    }
    if (levels.size > 0) {
      onFilters({ ...filters, levels });
    }
  };

  return (
    <div className="flex flex-wrap items-center gap-sm">
      <div role="group" aria-label={text.levelsLabel} className="flex gap-xs">
        {LEVELS.map((level) => {
          const on = filters.levels.has(level);
          return (
            <button
              key={level}
              type="button"
              aria-pressed={on}
              aria-label={text.levelToggle(text.levels[level].name, counts[level])}
              onClick={() => toggle(level)}
              className={`inline-flex h-control lift cursor-pointer items-center gap-xs rounded-md px-sm font-mono text-xs font-semibold outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
                on ? LEVEL_STYLES[level].on : "text-ink-muted line-through"
              }`}
            >
              {text.levels[level].code}
              <span className="font-regular tabular-nums">{counts[level]}</span>
            </button>
          );
        })}
      </div>
      <div className="relative min-w-0 flex-1 basis-(--tk-size-tile)">
        <Search
          aria-hidden
          className="pointer-events-none absolute top-1/2 left-md size-(--tk-font-size-sm) -translate-y-1/2 text-ink-muted"
        />
        <Input
          type="search"
          aria-label={text.keywordLabel}
          placeholder={text.keywordLabel}
          value={filters.query}
          onChange={(event) => onFilters({ ...filters, query: event.target.value })}
          className="pl-xl"
        />
      </div>
      <p className="text-sm text-ink-muted tabular-nums">{text.shown(shown, entries.length)}</p>
    </div>
  );
}

/**
 * The events as a list. On a wide screen each sits on one line: its time, its level and the message, which wraps. On a
 * narrow one the time and level take a line and the message the line below. The lines a message carries (a list of
 * reasons, an exception) keep their breaks.
 */
function EntryList({ entries, query }: { readonly entries: readonly ServiceLogEntry[]; readonly query: string }) {
  const levels = useDictionary().logs.levels;
  return (
    <ol className="grid font-mono text-xs leading-(--tk-line-height-normal)">
      {entries.map((entry, index) => {
        const style = LEVEL_STYLES[entry.level];
        return (
          <li
            // Events never move: a day's file is only appended to, so the position is the event's identity.
            key={index}
            className={`grid grid-cols-[auto_minmax(0,1fr)] gap-x-sm rounded-sm px-sm py-xs sm:grid-cols-[auto_auto_minmax(0,1fr)] ${style.row}`}
          >
            <span className="text-ink-muted tabular-nums">{clockOf(entry.time) ?? ""}</span>
            <abbr title={levels[entry.level].name} className={`font-semibold no-underline ${style.code}`}>
              {levels[entry.level].code}
            </abbr>
            <span className="col-span-2 break-words whitespace-pre-wrap sm:col-span-1">
              {highlightParts(entry.message, query).map((part, at) =>
                part.hit ? (
                  <mark key={at} className="rounded-xs bg-accent-soft text-accent">
                    {part.text}
                  </mark>
                ) : (
                  part.text
                ),
              )}
            </span>
          </li>
        );
      })}
    </ol>
  );
}

/** A day without a log file: it is past the days kept, or the service was not running. */
function NoLog({ today, date }: { readonly today: string; readonly date: string }) {
  const text = useDictionary().logs;
  return (
    <div className="grid justify-items-start gap-sm">
      <p className="text-sm text-ink-muted">{text.noLog(date)}</p>
      <Button asChild variant="outline">
        <Link to={logPathOf(today, today)}>{text.backToToday}</Link>
      </Button>
    </div>
  );
}

/** A day as the header shows it: the date with its weekday. */
function dayLabel(day: string, locale: string): string {
  return dayText(day, locale, { year: "numeric", month: "long", day: "numeric", weekday: "short" });
}
