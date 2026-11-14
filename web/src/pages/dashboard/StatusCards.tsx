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
  Server,
  TriangleAlert,
  Users,
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
import { clockOf, dayText, durationOf, fileNameOf, remainingOf, sizeOf, timeText, versionText } from "./format";
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
 * models can be probed as the saved configuration has them; what each probe found shows under its model.
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
      <CardActions note={dictionary.dashboard.probe.note}>
        <Button type="button" variant="outline" disabled={probe.isPending} onClick={() => probe.mutate()}>
          <BusyIcon busy={probe.isPending} icon={Activity} />
          {dictionary.dashboard.probe.button}
        </Button>
      </CardActions>
    </Card>
  );
}

/** One figure of a card: a large number over its label. */
function Figure({ label, value }: { readonly label: string; readonly value: number | null }) {
  return (
    <div className="grid gap-xs">
      <dt className="text-xs text-ink-muted">{label}</dt>
      <dd className="text-lg leading-(--tk-line-height-tight) font-semibold tabular-nums">{value ?? "—"}</dd>
    </div>
  );
}

/**
 * How long ago the memory was last consolidated, with the day, and what waits for the next: candidates, format problems,
 * weeks to summarize. The index can be rebuilt from here after a confirmation; the result shows as a toast, a failure (a
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
          <span className="text-lg leading-(--tk-line-height-tight) font-semibold">{text.daysAgo(consolidation.daysSince)}</span>
          <span className="text-xs text-ink-muted">
            {text.lastConsolidation(
              dayText(consolidation.lastConsolidationDay, locale, { dateStyle: "medium" }),
            )}
          </span>
        </div>
      )}
      {pending == null ? null : (
        <dl className="grid grid-cols-3 gap-md">
          <Figure label={text.candidates} value={pending.candidates} />
          <Figure label={text.formatProblems} value={pending.formatProblems} />
          <Figure label={text.unsummarizedWeeks} value={pending.unsummarizedWeeks ?? null} />
        </dl>
      )}
      <FailureAlert failure={failure} />
      <CardActions>
        <ReindexButton busy={reindex.isPending} onConfirm={() => reindex.mutate()} />
      </CardActions>
    </Card>
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
 * The backups: when the latest local backup was made, with its kind, its size and how many there are, and the last cloud
 * backup. The backup button opens a menu of the local and the cloud backup, the latter dimmed while it is not configured;
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
              <span className="text-xs text-ink-muted">
                {[text.kinds[latest.kind], sizeOf(latest.bytes, locale), text.count(list.local.length)].join(" · ")}
              </span>
            </div>
          )}
          <div className="flex items-center justify-between gap-md text-sm">
            <span className="text-ink-muted">{text.cloud}</span>
            {!list.remoteConfigured ? (
              <span className="text-ink-muted">{text.cloudNotConfigured}</span>
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

/** An address in the service card, with a button that copies it. */
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
 * The running service: how long it has run, its version, the MCP and web addresses, and the extra listener and whether
 * it started.
 */
export function ServiceCard({ report, className }: Omit<CardProps, "locale">) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.service;
  const service = report.service;

  return (
    <Card aria-labelledby="service-title" className={className}>
      <CardHeading id="service-title" icon={Server} title={dictionary.dashboard.cards.service} />
      {service == null ? (
        <Unreadable />
      ) : (
        <dl className="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-x-lg gap-y-xs text-sm">
          {service.uptimeSeconds === null ? null : (
            <InfoRow label={text.uptime}>{text.duration(durationOf(service.uptimeSeconds))}</InfoRow>
          )}
          {service.version === null ? null : (
            <InfoRow label={text.version}>
              <span title={service.version} className="truncate">
                {versionText(service.version)}
              </span>
            </InfoRow>
          )}
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
    </Card>
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

/**
 * The agents: each binding and each client that connected, merged into one row per agent with its logo, whether its binding
 * is sound, broken or missing, and when it last connected. With no binding, a line says what that means and a button leads
 * to the bindings in the settings.
 */
export function AgentsCard({ report, locale, className }: CardProps) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.agents;
  const bindings = report.bindings?.items;
  const rows = agentsOf(bindings ?? [], report.clients?.items ?? []);

  return (
    <Card aria-labelledby="agents-title" className={className}>
      <CardHeading id="agents-title" icon={Users} title={dictionary.dashboard.cards.clients} />
      {bindings === undefined && report.clients == null ? (
        <Unreadable />
      ) : (
        <>
          {rows.length === 0 ? null : (
            <ul className="grid gap-sm">
              {rows.map((row) => (
                <AgentItem key={row.key} row={row} seen={seenText(row.lastSeen, locale, text)} />
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
    </Card>
  );
}

function AgentItem({ row, seen }: { readonly row: AgentRow; readonly seen: string }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.agents;
  const problem = row.binding?.problem ?? null;

  return (
    <li className="flex items-center gap-md">
      <AgentLogo icon={row.icon} />
      <span className="grid min-w-0 flex-1 leading-(--tk-line-height-tight)">
        <span className="truncate text-sm font-medium">{row.label}</span>
        <span className="truncate text-xs text-ink-muted">
          {problem === null ? seen : <span className="text-danger">{dictionary.dashboard.bindingProblems[problem]}</span>}
        </span>
      </span>
      {row.binding === null ? (
        <Badge>
          <CircleDashed aria-hidden />
          {text.unbound}
        </Badge>
      ) : problem === null ? (
        <Badge tone="success">
          <Link2 aria-hidden />
          {text.bound}
        </Badge>
      ) : (
        <Badge tone="danger">
          <TriangleAlert aria-hidden />
          {text.broken}
        </Badge>
      )}
    </li>
  );
}
