import {
  Activity,
  Archive,
  BellRing,
  ChevronRight,
  CircleCheck,
  CloudAlert,
  Cpu,
  DatabaseZap,
  Link2Off,
  type LucideIcon,
  Network,
  Plug,
  Server,
  Unlink,
  Users,
} from "lucide-react";
import { useEffect } from "react";
import { Link, useLocation } from "react-router";
import { type StatusReport, useStatus } from "../../api/status";
import { useDictionary, useSessionStatus } from "../../app/session-context";
import { Alert, AlertDescription } from "../../components/ui/alert";
import { Badge } from "../../components/ui/badge";
import { Card, CardTitle } from "../../components/ui/card";
import type { Dictionary } from "../../i18n/messages";
import { durationOf } from "./duration";
import { targetOf, type Todo, todosOf } from "./todos";

/**
 * The dashboard. On a wide screen it is a bento grid of twelve columns: the left eight hold what to look at (the status
 * sentence, then the memory activity spanning two rows), the right four what to act on (to-dos, models, backups), and a
 * last row of four small cards. On a narrower screen the cards stack in one column, in the order of the markup. The status
 * report is fetched every 30 seconds; when a fetch fails, the cards keep the last report and a notice says so.
 */
export function DashboardPage() {
  const dictionary = useDictionary();
  const session = useSessionStatus();
  const status = useStatus();
  const report = status.data;
  useScrollToHash(report !== undefined);

  const locale = session.language === "zh" ? "zh-CN" : "en";
  const cards = dictionary.dashboard.cards;

  return (
    <div className="grid gap-lg">
      <h1 className="sr-only">{dictionary.nav.dashboard}</h1>
      {status.isError ? (
        <Alert tone="danger">
          <AlertDescription>
            {report === undefined
              ? dictionary.dashboard.connectionLostNever
              : dictionary.dashboard.connectionLost(
                  new Date(status.dataUpdatedAt).toLocaleTimeString(locale, { hour: "2-digit", minute: "2-digit" }),
                )}
          </AlertDescription>
        </Alert>
      ) : null}

      {report === undefined ? (
        status.isError ? null : <div aria-busy="true" className="min-h-dvh" />
      ) : (
        <div className="grid gap-lg xl:grid-cols-12">
          <StatusCard report={report} todos={todosOf(report)} className="xl:col-span-8" />
          <TodoCard todos={todosOf(report)} locale={locale} className="xl:col-span-4" />
          <PlaceholderCard icon={Cpu} title={cards.models} className="xl:col-span-4 xl:col-start-9 xl:row-start-2" />
          <PlaceholderCard
            id="backups"
            icon={Archive}
            title={cards.backups}
            className="xl:col-span-4 xl:col-start-9 xl:row-start-3"
          />
          <PlaceholderCard
            icon={Activity}
            title={cards.activity}
            className="xl:col-span-8 xl:col-start-1 xl:row-span-2 xl:row-start-2"
          />
          <PlaceholderCard icon={DatabaseZap} title={cards.index} className="xl:col-span-3" />
          <PlaceholderCard icon={Server} title={cards.service} className="xl:col-span-3" />
          <PlaceholderCard icon={Users} title={cards.clients} className="xl:col-span-3" />
          <PlaceholderCard icon={Plug} title={cards.bindings} className="xl:col-span-3" />
        </div>
      )}
    </div>
  );
}

/** The one sentence of the overall state: all is well, with the uptime and the last consolidation, or how many to-dos. */
function StatusCard({
  report,
  todos,
  className,
}: {
  readonly report: StatusReport;
  readonly todos: readonly Todo[];
  readonly className: string;
}) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard;
  const calm = todos.length === 0;
  const Icon = calm ? CircleCheck : BellRing;

  const uptime = report.service?.uptimeSeconds;
  const days = report.consolidation?.daysSince;
  const details = [
    uptime == null ? null : text.uptime(durationOf(uptime)),
    days == null ? text.neverConsolidated : text.consolidated(days),
  ].filter((detail) => detail !== null);

  return (
    <Card className={`content-center ${className}`}>
      <div className="flex items-center gap-md">
        <span
          className={`grid size-(--tk-size-control) shrink-0 place-items-center rounded-md ${
            calm ? "bg-success-soft text-success" : "bg-warning-soft text-warning"
          }`}
        >
          <Icon aria-hidden className="size-(--tk-font-size-lg)" />
        </span>
        <div className="grid gap-xs">
          <p className="text-xl leading-(--tk-line-height-tight) font-semibold">
            {calm ? text.calm : text.pending(todos.length)}
          </p>
          <p className="text-sm text-ink-muted">{details.join(" · ")}</p>
        </div>
      </div>
    </Card>
  );
}

/** The to-do card: each thing that needs the user, as a link to where it is fixed, or a sentence that nothing does. */
function TodoCard({
  todos,
  locale,
  className,
}: {
  readonly todos: readonly Todo[];
  readonly locale: string;
  readonly className: string;
}) {
  const dictionary = useDictionary();
  return (
    <Card aria-labelledby="todos-title" className={className}>
      <header className="flex items-center justify-between gap-sm">
        <CardTitle id="todos-title">{dictionary.dashboard.cards.todos}</CardTitle>
        {todos.length === 0 ? null : <Badge tone="warning">{todos.length}</Badge>}
      </header>
      {todos.length === 0 ? (
        <p className="flex items-center gap-sm text-sm text-ink-muted">
          <CircleCheck aria-hidden className="size-(--tk-font-size-base) text-success" />
          {dictionary.dashboard.todos.empty}
        </p>
      ) : (
        <ul className="-mx-sm grid gap-xs">
          {todos.map((todo, index) => {
            const view = viewOf(todo, dictionary, locale);
            return (
              <li key={index}>
                <Link
                  to={targetOf(todo)}
                  className="flex items-center gap-sm rounded-md p-sm transition-colors outline-none hover:bg-surface-raised focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
                >
                  <span
                    className={`grid size-(--tk-size-control) shrink-0 place-items-center rounded-sm ${
                      view.failure ? "bg-danger-soft text-danger" : "bg-warning-soft text-warning"
                    }`}
                  >
                    <view.icon aria-hidden className="size-(--tk-font-size-base)" />
                  </span>
                  <span className="grid min-w-0 flex-1 leading-(--tk-line-height-tight)">
                    <span className="text-sm font-medium">{view.title}</span>
                    {view.detail === null ? null : <span className="text-xs text-ink-muted">{view.detail}</span>}
                  </span>
                  <ChevronRight aria-hidden className="size-(--tk-font-size-base) shrink-0 text-ink-muted" />
                </Link>
              </li>
            );
          })}
        </ul>
      )}
    </Card>
  );
}

interface TodoView {
  readonly icon: LucideIcon;
  readonly title: string;
  readonly detail: string | null;
  /** Whether something failed (shown in the error tone) rather than is missing (shown in the warning tone). */
  readonly failure: boolean;
}

/** The icon and words of a to-do. An unconfigured model or a missing binding is not an error, so it takes the warning tone. */
function viewOf(todo: Todo, dictionary: Dictionary, locale: string): TodoView {
  const text = dictionary.dashboard.todos;
  switch (todo.kind) {
    case "modelUnconfigured":
      return {
        icon: Cpu,
        title: text.modelUnconfigured(dictionary.dashboard.models[todo.role] ?? todo.role),
        detail: text.modelUnconfiguredDetail,
        failure: false,
      };
    case "noBindings":
      return { icon: Link2Off, title: text.noBindings, detail: text.noBindingsDetail, failure: false };
    case "bindingProblem":
      return {
        icon: Unlink,
        title: text.bindingProblem(todo.client),
        detail: dictionary.dashboard.bindingProblems[todo.problem],
        failure: true,
      };
    case "extraListenerFailed":
      return { icon: Network, title: text.extraListenerFailed(todo.address), detail: todo.reason, failure: true };
    case "cloudBackupFailed":
      return {
        icon: CloudAlert,
        title: text.cloudBackupFailed,
        detail:
          todo.error ??
          text.cloudBackupFailedDetail(
            new Date(todo.at).toLocaleString(locale, { dateStyle: "medium", timeStyle: "short" }),
          ),
        failure: true,
      };
  }
}

/** The place of a card that a later step of the dashboard fills: its icon and name in a dashed outline. */
function PlaceholderCard({
  id,
  icon: Icon,
  title,
  className,
}: {
  readonly id?: string;
  readonly icon: LucideIcon;
  readonly title: string;
  readonly className: string;
}) {
  const dictionary = useDictionary();
  return (
    <section
      id={id}
      aria-label={title}
      className={`grid place-content-center justify-items-center gap-sm rounded-lg border-(length:--tk-border-thick) border-dashed border-line px-lg py-xl text-ink-muted ${className}`}
    >
      <Icon aria-hidden className="size-(--tk-font-size-xl)" />
      <span className="text-sm font-medium">{title}</span>
      <span className="text-xs">{dictionary.notImplemented}</span>
    </section>
  );
}

/**
 * Scrolls to the element the location's hash names once the cards are there, so a to-do that leads to a card on this page
 * reaches it. With reduced motion asked for, it jumps instead of gliding.
 */
function useScrollToHash(ready: boolean) {
  const { hash } = useLocation();
  useEffect(() => {
    if (ready && hash !== "") {
      const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
      document
        .getElementById(decodeURIComponent(hash.slice(1)))
        ?.scrollIntoView({ behavior: reduced ? "auto" : "smooth", block: "center" });
    }
  }, [ready, hash]);
}
