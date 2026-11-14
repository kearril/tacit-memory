import { CalendarDays, Check, ChevronDown, RefreshCw, ScrollText } from "lucide-react";
import { Link, Navigate, useNavigate, useParams } from "react-router";
import { ApiError } from "../../api/client";
import type { ServiceLogDays, ServiceLogEntry, ServiceLogLevel } from "../../api/logs";
import { useLogDay, useLogDays } from "../../api/logs";
import { useDictionary, useSessionStatus } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "../../components/ui/dropdown-menu";
import { errorTextOf } from "../../i18n/messages";
import { BusyIcon } from "../dashboard/parts";
import { dayText } from "../dashboard/format";
import { clockOf, dateOptions, LOGS_PATH, logDateOf, logPathOf } from "./day";

/** The colour of each level's code; a warning or an error also tints its whole row. */
const LEVEL_STYLES: Readonly<Record<ServiceLogLevel, { readonly code: string; readonly row: string }>> = {
  information: { code: "text-ink-muted", row: "" },
  warning: { code: "text-warning", row: "bg-warning-soft" },
  error: { code: "text-danger", row: "bg-danger-soft" },
};

/**
 * The logs page: one day of the service log, oldest event first, today unless the address names another day. The page
 * itself scrolls, so a long day reads like the file does. The page heading is for screen readers only, as the sidebar
 * already shows which page this is.
 */
export function LogsPage() {
  const dictionary = useDictionary();
  const param = useParams().date;
  const days = useLogDays();
  const named = logDateOf(param);
  const today = days.data?.today;
  if (param !== undefined && named !== undefined && named === today) {
    return <Navigate to={LOGS_PATH} replace />;
  }
  const date = param === undefined ? today : named;

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-page) gap-xl">
      <h1 className="sr-only">{dictionary.nav.logs}</h1>
      {days.isError ? (
        <LoadFailed error={days.error} busy={days.isFetching} onRetry={() => void days.refetch()} />
      ) : days.data === undefined ? (
        <div aria-busy="true" className="min-h-dvh" />
      ) : (
        <Card>
          <LogHeader days={days.data} date={date ?? param ?? days.data.today} />
          {date === undefined ? <NoLog today={days.data.today} date={param ?? ""} /> : <LogDay date={date} today={days.data.today} />}
        </Card>
      )}
    </div>
  );
}

/** The head of the card: the page's icon, the day shown, and the button that picks another. */
function LogHeader({ days, date }: { readonly days: ServiceLogDays; readonly date: string }) {
  const dictionary = useDictionary();
  const text = dictionary.logs;
  const locale = useSessionStatus().language === "zh" ? "zh-CN" : "en";
  const navigate = useNavigate();
  const label = (day: string) => (day === days.today ? `${text.today} · ${dayLabel(day, locale)}` : dayLabel(day, locale));

  return (
    <header className="grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-sm">
      <span className="grid size-(--tk-size-icon-tile) place-items-center rounded-sm bg-accent-soft text-accent">
        <ScrollText aria-hidden className="size-(--tk-font-size-sm)" />
      </span>
      <h2 className="truncate text-lg font-semibold">{label(date)}</h2>
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
            <DropdownMenuItem key={day} onSelect={() => void navigate(logPathOf(day, days.today))}>
              {day === date ? <Check aria-hidden /> : <span aria-hidden className="size-(--tk-font-size-sm)" />}
              {label(day)}
            </DropdownMenuItem>
          ))}
        </DropdownMenuContent>
      </DropdownMenu>
    </header>
  );
}

/** One day's events, or why there are none. */
function LogDay({ date, today }: { readonly date: string; readonly today: string }) {
  const dictionary = useDictionary();
  const day = useLogDay(date);

  if (day.isError) {
    return day.error instanceof ApiError && day.error.status === 404 ? (
      <NoLog today={today} date={date} />
    ) : (
      <LoadFailed error={day.error} busy={day.isFetching} onRetry={() => void day.refetch()} />
    );
  }
  if (day.data === undefined) {
    return <div aria-busy="true" className="min-h-dvh" />;
  }
  if (day.data.entries.length === 0) {
    return <p className="text-sm text-ink-muted">{date === today ? dictionary.logs.emptyToday : dictionary.logs.emptyDay}</p>;
  }
  return <EntryList entries={day.data.entries} />;
}

/**
 * The events as a list. On a wide screen each sits on one line: its time, its level and the message, which wraps. On a
 * narrow one the time and level take a line and the message the line below. The lines a message carries (a list of
 * reasons, an exception) keep their breaks.
 */
function EntryList({ entries }: { readonly entries: readonly ServiceLogEntry[] }) {
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
            <span className="col-span-2 break-words whitespace-pre-wrap sm:col-span-1">{entry.message}</span>
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

/** A read that failed: the service's explanation, and a button to read again. */
function LoadFailed({ error, busy, onRetry }: { readonly error: unknown; readonly busy: boolean; readonly onRetry: () => void }) {
  const dictionary = useDictionary();
  return (
    <Alert tone="danger">
      <AlertTitle>{dictionary.logs.loadFailed}</AlertTitle>
      <AlertDescription className="grid gap-sm">
        <p>{errorTextOf(error, dictionary)}</p>
        <div>
          <Button type="button" variant="outline" disabled={busy} onClick={onRetry}>
            <BusyIcon busy={busy} icon={RefreshCw} />
            {dictionary.logs.readAgain}
          </Button>
        </div>
      </AlertDescription>
    </Alert>
  );
}

/** A day as the header shows it: the date with its weekday. */
function dayLabel(day: string, locale: string): string {
  return dayText(day, locale, { year: "numeric", month: "long", day: "numeric", weekday: "short" });
}
