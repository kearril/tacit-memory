import {
  BellRing,
  ChevronRight,
  CircleCheck,
  CloudAlert,
  Cpu,
  FileWarning,
  Link2Off,
  type LucideIcon,
  Network,
  Unlink,
} from "lucide-react";
import { Link } from "react-router";
import { useActivity } from "../../api/activity";
import { useBackups } from "../../api/backups";
import { type StatusReport, useStatus } from "../../api/status";
import { useScrollToHash } from "../../app/use-scroll-to-hash";
import { useDictionary, useSessionStatus } from "../../app/session-context";
import { Alert, AlertDescription } from "../../components/ui/alert";
import { Card } from "../../components/ui/card";
import type { Dictionary } from "../../i18n/messages";
import { durationOf, timeText } from "./format";
import { HeatmapCard } from "./HeatmapCard";
import { BackupsCard, ConnectionCard, IndexCard, LogsCard, ModelsCard } from "./StatusCards";
import { targetOf, type Todo, todosOf } from "./todos";

/**
 * The dashboard. On a wide screen it is a bento grid of twelve columns: the overview (the status sentence and the to-dos)
 * and the memory activity each across the whole width, then a row of three cards (models, backups, index and
 * consolidation), and the connection (the service's addresses, and the agents with their bindings) across the width. On a
 * narrower screen the cards stack in one column in the order of the markup, which puts what to act on (the to-dos, models
 * and backups) before the activity.
 * The status report is fetched every 30 seconds, the activity and the backups when the window regains focus; when a fetch
 * fails, the cards keep what they last had and a notice at the top says so, with the time of the oldest data shown.
 */
export function DashboardPage() {
  const dictionary = useDictionary();
  const session = useSessionStatus();
  const status = useStatus();
  const report = status.data;
  useScrollToHash(report !== undefined);
  // The cards read the same queries; asking for them here only adds them to the notice, not another request.
  const stale = [status, useActivity(), useBackups()].filter((query) => query.isError && query.data !== undefined);

  const locale = session.language === "zh" ? "zh-CN" : "en";

  return (
    <div className="grid gap-lg">
      <h1 className="sr-only">{dictionary.nav.dashboard}</h1>
      {status.isError && report === undefined ? (
        <Alert tone="danger">
          <AlertDescription>{dictionary.dashboard.connectionLostNever}</AlertDescription>
        </Alert>
      ) : stale.length > 0 ? (
        <Alert tone="danger">
          <AlertDescription>
            {dictionary.dashboard.connectionLost(
              new Date(Math.min(...stale.map((query) => query.dataUpdatedAt))).toLocaleTimeString(locale, {
                hour: "2-digit",
                minute: "2-digit",
              }),
            )}
          </AlertDescription>
        </Alert>
      ) : null}

      {report === undefined ? (
        status.isError ? null : <div aria-busy="true" className="min-h-dvh" />
      ) : (
        <div className="grid gap-lg xl:grid-cols-12">
          <OverviewCard report={report} locale={locale} className="xl:col-span-12" />
          <ModelsCard report={report} fetchedAt={status.dataUpdatedAt} className="xl:col-span-4 xl:row-start-3" />
          <BackupsCard locale={locale} className="xl:col-span-4 xl:row-start-3" />
          <HeatmapCard className="xl:col-span-12 xl:row-start-2" />
          <IndexCard report={report} locale={locale} className="xl:col-span-4" />
          <ConnectionCard report={report} locale={locale} className="xl:col-span-8" />
          <LogsCard report={report} className="xl:col-span-4" />
        </div>
      )}
    </div>
  );
}

/**
 * The overview across the top: the one sentence of the overall state on the left (all is well, with the uptime and the last
 * consolidation, or how many things need the user), and the to-dos beside it as compact cards in a row that wraps, each a
 * link to where it is fixed. On a narrow screen the to-dos go under the sentence.
 */
function OverviewCard({
  report,
  locale,
  className,
}: {
  readonly report: StatusReport;
  readonly locale: string;
  readonly className: string;
}) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard;
  const todos = todosOf(report);
  const calm = todos.length === 0;
  const Icon = calm ? CircleCheck : BellRing;

  const uptime = report.service?.uptimeSeconds;
  const days = report.consolidation?.daysSince;
  const details = [
    uptime == null ? null : text.uptime(durationOf(uptime)),
    days == null ? text.neverConsolidated : text.consolidated(days),
  ].filter((detail) => detail !== null);

  return (
    <Card aria-labelledby="overview-title" className={`py-md lg:grid-cols-[auto_minmax(0,1fr)] lg:items-center ${className}`}>
      <div className="flex items-center gap-md lg:pr-lg">
        <span
          className={`grid size-(--tk-size-control) shrink-0 place-items-center rounded-md ${
            calm ? "bg-success-soft text-success" : "bg-warning-soft text-warning"
          }`}
        >
          <Icon aria-hidden className="size-(--tk-font-size-lg)" />
        </span>
        <div className="grid gap-xs">
          <h2 id="overview-title" className="text-lg leading-(--tk-line-height-tight) font-semibold whitespace-nowrap">
            {calm ? text.calm : text.pending(todos.length)}
          </h2>
          <p className="text-sm whitespace-nowrap text-ink-muted">{details.join(" · ")}</p>
        </div>
      </div>
      {calm ? (
        <p className="text-sm text-ink-muted lg:border-l-(length:--tk-border-thin) lg:border-line lg:pl-lg">
          {text.todos.empty}
        </p>
      ) : (
        <ul
          aria-label={text.cards.todos}
          className="grid grid-cols-[repeat(auto-fill,minmax(var(--tk-size-tile),1fr))] gap-sm lg:border-l-(length:--tk-border-thin) lg:border-line lg:pl-lg"
        >
          {todos.map((todo, index) => (
            <li key={index} className="min-w-0">
              <TodoItem todo={todo} locale={locale} />
            </li>
          ))}
        </ul>
      )}
    </Card>
  );
}

/** One to-do as a compact card that links to where it is fixed: its icon, what it is, and one line on what to do. */
function TodoItem({ todo, locale }: { readonly todo: Todo; readonly locale: string }) {
  const dictionary = useDictionary();
  const view = viewOf(todo, dictionary, locale);
  return (
    <Link
      to={targetOf(todo)}
      title={view.detail === null ? view.title : `${view.title}\n${view.detail}`}
      className="lift flex items-center gap-sm rounded-md border-(length:--tk-border-thin) border-line bg-surface px-sm py-xs shadow-pressed outline-none hover:shadow-hover focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
    >
      <span
        className={`grid size-(--tk-size-icon-tile) shrink-0 place-items-center rounded-sm ${
          view.failure ? "bg-danger-soft text-danger" : "bg-warning-soft text-warning"
        }`}
      >
        <view.icon aria-hidden className="size-(--tk-font-size-sm)" />
      </span>
      <span className="grid min-w-0 flex-1 leading-(--tk-line-height-tight)">
        <span className="truncate text-sm font-medium">{view.title}</span>
        {view.detail === null ? null : <span className="truncate text-xs text-ink-muted">{view.detail}</span>}
      </span>
      <ChevronRight aria-hidden className="size-(--tk-font-size-sm) shrink-0 text-ink-muted" />
    </Link>
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
    case "modelsUnconfigured": {
      const names = todo.roles.map((role) => dictionary.dashboard.models[role] ?? role);
      return {
        icon: Cpu,
        title: text.modelsUnconfigured(names),
        detail: text.modelsUnconfiguredDetail(names),
        failure: false,
      };
    }
    case "noBindings":
      return { icon: Link2Off, title: text.noBindings, detail: text.noBindingsDetail, failure: false };
    case "bindingProblem":
      return {
        icon: Unlink,
        title: text.bindingProblem(todo.client),
        detail: dictionary.dashboard.bindingProblems[todo.problem],
        failure: true,
      };
    case "promptProblem":
      return {
        icon: FileWarning,
        title: text.promptProblem(dictionary.prompts.documents[todo.name].title),
        detail: todo.problem,
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
            timeText(todo.at, locale),
          ),
        failure: true,
      };
  }
}
