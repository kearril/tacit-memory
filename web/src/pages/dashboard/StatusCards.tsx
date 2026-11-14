import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Activity,
  Archive,
  ChevronDown,
  CircleCheck,
  CloudUpload,
  HardDriveDownload,
  RefreshCw,
  CircleDashed,
  Cpu,
  DatabaseZap,
  Hourglass,
  Link2,
  type LucideIcon,
  Cable,
  TriangleAlert,
} from "lucide-react";
import { type ReactNode, useEffect, useState } from "react";
import { Link } from "react-router";
import { BACKUPS_QUERY_KEY, type BackupTarget, runBackup, runReindex, useBackups } from "../../api/backups";
import { runProbe } from "../../api/probe";
import { STATUS_QUERY_KEY, type StatusReport } from "../../api/status";
import { useDictionary } from "../../app/session-context";
import { AgentLogo } from "./AgentLogo";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "../../components/ui/alert-dialog";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import { Card, CardTitle } from "../../components/ui/card";
import { CopyButton } from "../../components/ui/copy-button";
import { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger } from "../../components/ui/dropdown-menu";
import { useToast } from "../../components/ui/toast-context";
import { errorTextOf } from "../../i18n/messages";
import { type AgentRow, agentsOf } from "./agents";
import { clockOf, dayText, fileNameOf, remainingOf, sizeOf, timeText, versionText } from "./format";
import { BusyIcon, CardActions, InfoRow, ProbeLine } from "./parts";
import { RestoreDialog } from "./RestoreDialog";

interface CardProps {
  readonly report: StatusReport;
  readonly locale: string;
  readonly className: string;
}

/** The head of a status card: its icon on a soft tile, its name, and on the right a badge when there is something to say. */
function CardHeading({
  id,
  icon: Icon,
  title,
  badge,
}: {
  readonly id: string;
  readonly icon: LucideIcon;
  readonly title: string;
  readonly badge?: ReactNode;
}) {
  return (
    <header className="flex items-center gap-sm">
      <span className="grid size-(--tk-size-icon-tile) shrink-0 place-items-center rounded-sm bg-accent-soft text-accent">
        <Icon aria-hidden className="size-(--tk-font-size-sm)" />
      </span>
      <CardTitle id={id} className="min-w-0 flex-1 truncate text-base">
        {title}
      </CardTitle>
      {badge}
    </header>
  );
}

/** What a card shows in place of a column the status report left out because it could not be read. */
function Unreadable() {
  const dictionary = useDictionary();
  return <p className="text-sm text-ink-muted">{dictionary.dashboard.unreadable}</p>;
}

/** The current time, moving on every second while `running` is true. */
function useNow(running: boolean): number {
  const [now, setNow] = useState(() => Date.now());
  useEffect(() => {
    if (!running) {
      return;
    }
    const timer = window.setInterval(() => setNow(Date.now()), 1000);
    return () => window.clearInterval(timer);
  }, [running]);
  return now;
}

/** A failure to show in a card: what did not succeed, and the service's explanation as it is. */
interface Failure {
  readonly title: string;
  readonly message: string;
}

function FailureAlert({ failure }: { readonly failure: Failure | null }) {
  return failure === null ? null : (
    <Alert tone="danger">
      <AlertTitle>{failure.title}</AlertTitle>
      <AlertDescription>{failure.message}</AlertDescription>
    </Alert>
  );
}

/**
 * The three auxiliary models, each available, cooling down or not configured; the three look different in icon and words,
 * not only in colour, and not configured is not an error. A cooldown counts down every second from the report; when one
 * reaches zero the report is fetched again. While a model cools down, the card is outlined in the warning colour. The
 * models can be probed; what each probe found shows under its model.
 */
export function ModelsCard({ report, fetchedAt, className }: Omit<CardProps, "locale"> & { readonly fetchedAt: number }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.modelStates;
  const queryClient = useQueryClient();
  const items = report.models?.items;
  const cooling = items?.some((model) => model.state === "coolingDown") ?? false;
  const now = Math.max(useNow(cooling), fetchedAt);
  const [failure, setFailure] = useState<Failure | null>(null);
  const probe = useMutation({
    mutationFn: runProbe,
    onMutate: () => setFailure(null),
    onError: (error) => setFailure({ title: dictionary.dashboard.probe.failedTitle, message: errorTextOf(error, dictionary) }),
  });

  const remaining = (items ?? []).map((model) =>
    model.state === "coolingDown" && model.coolingSeconds !== null ? remainingOf(model.coolingSeconds, fetchedAt, now) : null,
  );
  const expired = remaining.some((seconds) => seconds === 0);
  useEffect(() => {
    if (expired) {
      void queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY });
    }
  }, [expired, fetchedAt, queryClient]);

  return (
    <Card
      aria-labelledby="models-title"
      className={`flex flex-col ${cooling ? "ring-(length:--tk-border-thin) ring-warning" : ""} ${className}`}
    >
      <CardHeading
        id="models-title"
        icon={Cpu}
        title={dictionary.dashboard.cards.models}
        badge={
          cooling ? (
            <Badge tone="warning">
              <Hourglass aria-hidden />
              {text.cooling}
            </Badge>
          ) : null
        }
      />
      {items === undefined ? (
        <Unreadable />
      ) : (
        <ul className="grid gap-sm">
          {items.map((model, index) => {
            const result = probe.data?.results.find((candidate) => candidate.role === model.role);
            return (
              <li key={model.role} className="grid min-w-0">
                <span className="flex items-center justify-between gap-md text-sm">
                  <span className="truncate">{dictionary.dashboard.models[model.role] ?? model.role}</span>
                  {model.state === "available" ? (
                    <Badge tone="success">
                      <CircleCheck aria-hidden />
                      {text.available}
                    </Badge>
                  ) : model.state === "coolingDown" ? (
                    <Badge tone="warning" className="tabular-nums">
                      <Hourglass aria-hidden />
                      {text.coolingDown(clockOf(remaining[index] ?? 0))}
                    </Badge>
                  ) : (
                    <Badge>
                      <CircleDashed aria-hidden />
                      {text.unconfigured}
                    </Badge>
                  )}
                </span>
                {result === undefined ? null : <ProbeLine result={result} />}
              </li>
            );
          })}
        </ul>
      )}
      <FailureAlert failure={failure} />
      <CardActions>
        <Button type="button" variant="outline" disabled={probe.isPending} onClick={() => probe.mutate()}>
          <BusyIcon busy={probe.isPending} icon={Activity} />
          {dictionary.dashboard.probe.button}
        </Button>
      </CardActions>
    </Card>
  );
}

/**
 * How long ago the memory was last consolidated, with the day once it is more than a day ago, and what waits for the next
 * consolidation in words: candidates, files with format problems, weeks to summarize, each only when there is any, or one
 * line that all is well. The index can be rebuilt from here after a confirmation; the result shows as a toast, a failure (a
 * maintenance operation already running among them) as the service's explanation in the card.
 */
export function IndexCard({ report, locale, className }: CardProps) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.index;
  const consolidation = report.consolidation;
  const pending = report.pending;
  const showToast = useToast();
  const queryClient = useQueryClient();
  const [failure, setFailure] = useState<Failure | null>(null);
  const reindex = useMutation({
    mutationFn: runReindex,
    onMutate: () => setFailure(null),
    onSuccess: (result) => {
      showToast(dictionary.dashboard.reindex.done(result.files, result.units));
      void queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY });
    },
    onError: (error) => setFailure({ title: dictionary.dashboard.reindex.failed, message: errorTextOf(error, dictionary) }),
  });

  return (
    <Card aria-labelledby="index-title" className={`flex flex-col ${className}`}>
      <CardHeading id="index-title" icon={DatabaseZap} title={dictionary.dashboard.cards.index} />
      {consolidation == null ? (
        <Unreadable />
      ) : consolidation.lastConsolidationDay === null || consolidation.daysSince === null ? (
        <span className="text-lg font-semibold">{text.never}</span>
      ) : (
        <div className="grid gap-xs">
          <span className="text-xs text-ink-muted">{text.lastConsolidation}</span>
          <span className="text-lg leading-(--tk-line-height-tight) font-semibold">
            {text.daysAgo(consolidation.daysSince)}
            {consolidation.daysSince > 1 ? (
              <span className="ml-sm text-sm font-normal text-ink-muted">
                {dayText(consolidation.lastConsolidationDay, locale, { dateStyle: "medium" })}
              </span>
            ) : null}
          </span>
        </div>
      )}
      {pending == null ? null : <Waiting pending={pending} />}
      <FailureAlert failure={failure} />
      <CardActions>
        <ReindexButton busy={reindex.isPending} onConfirm={() => reindex.mutate()} />
      </CardActions>
    </Card>
  );
}

/** What waits for the next consolidation, a line for each kind there is any of; format problems in the warning colour. */
function Waiting({ pending }: { readonly pending: NonNullable<StatusReport["pending"]> }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.index;
  const lines = [
    { count: pending.candidates ?? 0, words: text.candidates, warn: false },
    { count: pending.formatProblems ?? 0, words: text.formatProblems, warn: true },
    { count: pending.unsummarizedWeeks ?? 0, words: text.unsummarizedWeeks, warn: false },
  ].filter((line) => line.count > 0);

  return lines.length === 0 ? (
    <p className="flex items-center gap-xs text-sm text-ink-muted">
      <CircleCheck aria-hidden className="size-(--tk-font-size-sm) text-success" />
      {text.clear}
    </p>
  ) : (
    <ul className="grid gap-xs text-sm">
      {lines.map((line) => (
        <li key={line.words(0)} className={line.warn ? "text-warning" : undefined}>
          {line.words(line.count)}
        </li>
      ))}
    </ul>
  );
}

/** The rebuild button and its confirmation, whose confirm button names the action itself. */
function ReindexButton({ busy, onConfirm }: { readonly busy: boolean; readonly onConfirm: () => void }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.reindex;
  return (
    <AlertDialog>
      <AlertDialogTrigger asChild>
        <Button type="button" variant="outline" disabled={busy}>
          <BusyIcon busy={busy} icon={RefreshCw} />
          {text.button}
        </Button>
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{text.title}</AlertDialogTitle>
          <AlertDialogDescription>{text.body}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{text.cancel}</AlertDialogCancel>
          <AlertDialogAction onClick={onConfirm}>{text.confirm}</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}

/**
 * The backups: when the latest local backup was made, with its kind and its size, how many there are, and the last cloud
 * backup; while the cloud backup is not configured, a link leads to it in the settings. The backup button opens a menu of the local and the cloud backup, the latter dimmed while it is not configured;
 * the one chosen runs at once, without a confirmation. While it runs, the button waits; the result shows as a toast, a
 * failure as the service's explanation in the card. A backup can be restored from here, through the restore dialog.
 */
export function BackupsCard({ locale, className }: { readonly locale: string; readonly className: string }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.backups;
  const backups = useBackups();
  const showToast = useToast();
  const queryClient = useQueryClient();
  const [failure, setFailure] = useState<Failure | null>(null);
  const backup = useMutation({
    mutationFn: (target: BackupTarget) => runBackup(target),
    onMutate: () => setFailure(null),
    onSuccess: (result, target) => {
      showToast(target === "remote" ? text.cloudBackedUp : text.backedUp(fileNameOf(result.location)));
    },
    onError: (error, target) =>
      setFailure({ title: target === "remote" ? text.cloudFailedTitle : text.failed, message: errorTextOf(error, dictionary) }),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: BACKUPS_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY });
    },
  });
  const list = backups.data;
  const latest = list?.local[0];
  const lastRemote = list?.lastRemote ?? null;

  return (
    <Card id="backups" aria-labelledby="backups-title" className={`flex flex-col ${className}`}>
      <CardHeading id="backups-title" icon={Archive} title={dictionary.dashboard.cards.backups} />
      {list === undefined ? (
        backups.isError ? (
          <FailureAlert failure={{ title: dictionary.dashboard.unreadable, message: errorTextOf(backups.error, dictionary) }} />
        ) : (
          <div aria-busy="true" className="py-lg" />
        )
      ) : (
        <>
          {latest === undefined ? (
            <span className="text-lg font-semibold">{text.none}</span>
          ) : (
            <div className="grid gap-xs">
              <span className="text-lg leading-(--tk-line-height-tight) font-semibold">{timeText(latest.at, locale)}</span>
              <span className="text-xs text-ink-muted">{text.latest(text.kinds[latest.kind], sizeOf(latest.bytes, locale))}</span>
              <span className="text-xs text-ink-muted">{text.count(list.local.length)}</span>
            </div>
          )}
          <div className="flex items-center justify-between gap-md text-sm">
            <span className="text-ink-muted">{text.cloud}</span>
            {!list.remoteConfigured ? (
              <span className="flex items-center gap-sm">
                <span className="text-ink-muted">{text.cloudNotConfigured}</span>
                <Link to="/settings#settings-backup" className="text-accent underline-offset-2 hover:underline">
                  {text.configure}
                </Link>
              </span>
            ) : lastRemote === null ? (
              <span className="text-ink-muted">{text.cloudNever}</span>
            ) : (
              <span className="flex min-w-0 items-center gap-sm">
                <span className="truncate text-xs text-ink-muted">{timeText(lastRemote.at, locale)}</span>
                {lastRemote.succeeded ? (
                  <Badge tone="success">
                    <CircleCheck aria-hidden />
                    {text.cloudSucceeded}
                  </Badge>
                ) : (
                  <Badge tone="danger" title={lastRemote.error ?? undefined}>
                    <TriangleAlert aria-hidden />
                    {text.cloudFailed}
                  </Badge>
                )}
              </span>
            )}
          </div>
          <FailureAlert failure={failure} />
          <CardActions>
            <RestoreDialog list={list} locale={locale} />
            <DropdownMenu>
              <DropdownMenuTrigger asChild>
                <Button type="button" disabled={backup.isPending}>
                  <BusyIcon busy={backup.isPending} icon={HardDriveDownload} />
                  {text.backUpNow}
                  <ChevronDown aria-hidden />
                </Button>
              </DropdownMenuTrigger>
              <DropdownMenuContent>
                <DropdownMenuItem onSelect={() => backup.mutate("local")}>
                  <HardDriveDownload aria-hidden />
                  {text.backUpLocal}
                </DropdownMenuItem>
                <DropdownMenuItem
                  disabled={!list.remoteConfigured}
                  hint={list.remoteConfigured ? undefined : text.cloudNotConfigured}
                  onSelect={() => backup.mutate("remote")}
                >
                  <CloudUpload aria-hidden />
                  {text.backUpCloud}
                </DropdownMenuItem>
              </DropdownMenuContent>
            </DropdownMenu>
          </CardActions>
        </>
      )}
    </Card>
  );
}

/** An address of the service, with a button that copies it. */
function Address({ value, label }: { readonly value: string; readonly label: string }) {
  const dictionary = useDictionary();
  return (
    <>
      <span title={value} className="min-w-0 truncate select-all">
        {value}
      </span>
      <CopyButton value={value} label={dictionary.dashboard.copy(label)} copiedMessage={dictionary.dashboard.copied(label)} />
    </>
  );
}

/**
 * How clients reach the memory, in one card: the service's version in the head; on the left its MCP and web addresses and
 * the extra listener, whether it started; on the right the agents, each binding and each client that connected merged into
 * one tile per agent with its logo, whether its binding is sound or broken on the logo's corner, and when it last
 * connected. On a narrow screen the agents go under the addresses. With no binding, a line says what that means and a
 * button leads to the bindings in the settings.
 */
export function ConnectionCard({ report, locale, className }: CardProps) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.service;
  const service = report.service;

  return (
    <Card aria-labelledby="connection-title" className={className}>
      <CardHeading
        id="connection-title"
        icon={Cable}
        title={dictionary.dashboard.cards.connection}
        badge={
          service?.version == null ? null : (
            <span title={service.version} className="truncate text-xs text-ink-muted">
              {text.version(versionText(service.version))}
            </span>
          )
        }
      />
      <div className="grid gap-lg lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        {service == null ? (
          <Unreadable />
        ) : (
          <dl className="grid content-start grid-cols-[auto_minmax(0,1fr)] items-center gap-x-lg gap-y-xs text-sm">
            {service.endpoint === null ? null : (
              <InfoRow label={text.mcp}>
                <Address value={service.endpoint} label={text.mcp} />
              </InfoRow>
            )}
            <InfoRow label={text.web}>
              <Address value={service.webAddress} label={text.web} />
            </InfoRow>
            <InfoRow label={text.extraListener}>
              {service.extraListener === null ? (
                <span className="text-ink-muted">{text.noExtraListener}</span>
              ) : (
                <>
                  <Address value={service.extraListener.address} label={text.extraListener} />
                  {service.extraListener.failureReason === null ? (
                    <Badge tone="success">
                      <CircleCheck aria-hidden />
                      {text.listening}
                    </Badge>
                  ) : (
                    <Badge tone="danger" title={service.extraListener.failureReason}>
                      <TriangleAlert aria-hidden />
                      {text.listenFailed}
                    </Badge>
                  )}
                </>
              )}
            </InfoRow>
          </dl>
        )}
        <Agents report={report} locale={locale} />
      </div>
    </Card>
  );
}

/** The agents of the connection card, under a small heading, beside the addresses on a wide screen. */
function Agents({ report, locale }: { readonly report: StatusReport; readonly locale: string }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.agents;
  const bindings = report.bindings?.items;
  const rows = agentsOf(bindings ?? [], report.clients?.items ?? []);

  return (
    <section
      aria-labelledby="agents-title"
      className="grid content-start gap-sm lg:border-l-(length:--tk-border-thin) lg:border-line lg:pl-lg"
    >
      <h3 id="agents-title" className="text-xs text-ink-muted">
        {text.title}
      </h3>
      {bindings === undefined && report.clients == null ? (
        <Unreadable />
      ) : (
        <>
          {rows.length === 0 ? null : (
            <ul className="grid grid-cols-[repeat(auto-fill,minmax(calc(var(--tk-size-logo)*2.5),1fr))] gap-sm">
              {rows.map((row) => (
                <AgentTile key={row.key} row={row} seen={seenText(row.lastSeen, locale, text)} />
              ))}
            </ul>
          )}
          {bindings !== undefined && bindings.length === 0 ? (
            <div className="flex flex-wrap items-center justify-between gap-sm">
              <p className="text-sm text-ink-muted">{rows.length === 0 ? text.empty : text.noBindings}</p>
              <Button asChild variant="outline">
                <Link to="/settings#settings-bindings">
                  <Link2 aria-hidden />
                  {text.addBinding}
                </Link>
              </Button>
            </div>
          ) : null}
        </>
      )}
    </section>
  );
}

/**
 * One agent as a tile: its logo with the binding's state on the corner, its name, and when it last connected; a broken
 * binding says what is wrong in that line instead, and an agent with no binding says so.
 */
function AgentTile({ row, seen }: { readonly row: AgentRow; readonly seen: string }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.agents;
  const problem = row.binding?.problem ?? null;
  const state = row.binding === null ? text.unbound : problem === null ? text.bound : text.broken;

  return (
    <li title={`${row.label}\n${state}`} className="grid min-w-0 justify-items-center gap-xs rounded-md p-sm text-center">
      <AgentLogo icon={row.icon} large sound={row.binding === null ? undefined : problem === null} />
      <span className="sr-only">{state}</span>
      <span className="w-full truncate text-sm font-medium">{row.label}</span>
      <span className="w-full truncate text-xs text-ink-muted">
        {problem !== null ? (
          <span className="text-danger">{dictionary.dashboard.bindingProblems[problem]}</span>
        ) : row.binding === null ? (
          text.unboundSeen(seen)
        ) : (
          seen
        )}
      </span>
    </li>
  );
}

/** A day as the date the service gave it, in the browser's own calendar: today, yesterday, or the date. */
function seenText(day: string | null, locale: string, text: ReturnType<typeof useDictionary>["dashboard"]["agents"]): string {
  if (day === null) {
    return text.neverSeen;
  }
  const dayOf = (offset: number) => {
    const date = new Date();
    date.setDate(date.getDate() - offset);
    return `${date.getFullYear()}-${String(date.getMonth() + 1).padStart(2, "0")}-${String(date.getDate()).padStart(2, "0")}`;
  };
  if (day === dayOf(0)) {
    return text.today;
  }
  if (day === dayOf(1)) {
    return text.yesterday;
  }
  return text.lastSeen(
    dayText(day, locale, { month: "short", day: "numeric" }),
  );
}
