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
  ScrollText,
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
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from "../../components/ui/dropdown-menu";
import { useToast } from "../../components/ui/toast-context";
import { errorTextOf } from "../../i18n/messages";
import { type AgentRow, agentsOf } from "./agents";
import { clockOf, dayText, fileNameOf, remainingOf, sizeOf, timeText, versionText } from "./format";
import { BusyIcon, IconAction, InfoRow, ProbeLine } from "./parts";
import { RestoreDialog } from "./RestoreDialog";
import { LOGS_PATH } from "../logs/day";
import { CANDIDATES_ANCHOR, memoryPathOf } from "../memory/views";
import { searchOf } from "../logs/filter";

interface CardProps {
  readonly report: StatusReport;
  readonly locale: string;
  readonly className: string;
}

/**
 * The head of a status card: its icon on a soft tile, its name, on the right a badge when there is something to say, and
 * at the end the card's commands. The commands sit beside the name rather than at the bottom, so nothing is pinned to the
 * foot of a card and a short card in a row of taller ones simply ends sooner.
 */
function CardHeading({
  id,
  icon: Icon,
  title,
  badge,
  actions,
}: {
  readonly id: string;
  readonly icon: LucideIcon;
  readonly title: string;
  readonly badge?: ReactNode;
  readonly actions?: ReactNode;
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
      {actions === undefined ? null : <div className="flex shrink-0 items-center gap-xs">{actions}</div>}
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
 * models can be probed from the card's head; what each probe found shows under its model.
 */
export function ModelsCard({
  report,
  fetchedAt,
  className,
}: Omit<CardProps, "locale"> & { readonly fetchedAt: number }) {
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
    onError: (error) =>
      setFailure({ title: dictionary.dashboard.probe.failedTitle, message: errorTextOf(error, dictionary) }),
  });

  const remaining = (items ?? []).map((model) =>
    model.state === "coolingDown" && model.coolingSeconds !== null
      ? remainingOf(model.coolingSeconds, fetchedAt, now)
      : null,
  );
  const expired = remaining.some((seconds) => seconds === 0);
  useEffect(() => {
    if (expired) {
      void queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY });
    }
  }, [expired, fetchedAt, queryClient]);

  return (
    <Card
      variant="flat"
      aria-labelledby="models-title"
      className={`${cooling ? "ring-(length:--tk-border-thin) ring-warning" : ""} ${className}`}
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
        actions={
          <IconAction
            type="button"
            label={dictionary.dashboard.probe.button}
            disabled={probe.isPending}
            onClick={() => probe.mutate()}
          >
            <BusyIcon busy={probe.isPending} icon={Activity} />
          </IconAction>
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
    onError: (error) =>
      setFailure({ title: dictionary.dashboard.reindex.failed, message: errorTextOf(error, dictionary) }),
  });

  return (
    <Card variant="flat" aria-labelledby="index-title" className={className}>
      <CardHeading
        id="index-title"
        icon={DatabaseZap}
        title={dictionary.dashboard.cards.index}
        actions={<ReindexButton busy={reindex.isPending} onConfirm={() => reindex.mutate()} />}
      />
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
              <span className="ml-sm text-sm font-regular text-ink-muted">
                {dayText(consolidation.lastConsolidationDay, locale, { dateStyle: "medium" })}
              </span>
            ) : null}
          </span>
        </div>
      )}
      {pending == null ? null : <Waiting pending={pending} />}
      <FailureAlert failure={failure} />
    </Card>
  );
}

/**
 * The service log at a glance: how many warnings and errors today's file holds. The link in the head opens the logs page
 * showing only those when there are any, and the whole day otherwise. When today's file cannot be read, the card says so.
 */
export function LogsCard({ report, className }: Omit<CardProps, "locale">) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.logs;
  const logs =
    report.logs?.warnings == null || report.logs.errors == null
      ? undefined
      : { warnings: report.logs.warnings, errors: report.logs.errors };
  const notable = logs === undefined ? 0 : logs.warnings + logs.errors;

  return (
    <Card variant="flat" aria-labelledby="logs-title" className={className}>
      <CardHeading
        id="logs-title"
        icon={ScrollText}
        title={dictionary.dashboard.cards.logs}
        actions={
          <IconAction asChild label={text.view}>
            <Link
              to={
                notable === 0
                  ? LOGS_PATH
                  : `${LOGS_PATH}${searchOf({ levels: new Set(["warning", "error"]), query: "" })}`
              }
            >
              <ScrollText aria-hidden />
            </Link>
          </IconAction>
        }
      />
      {logs === undefined ? (
        <Unreadable />
      ) : (
        <div className="grid gap-xs">
          <span className="text-xs text-ink-muted">{text.today}</span>
          {notable === 0 ? (
            <span className="text-lg leading-(--tk-line-height-tight) font-semibold">{text.quiet}</span>
          ) : (
            <span className="flex flex-wrap gap-x-md text-lg leading-(--tk-line-height-tight) font-semibold">
              <span className={logs.warnings > 0 ? "text-warning" : "text-ink-muted"}>
                {text.warnings(logs.warnings)}
              </span>
              <span className={logs.errors > 0 ? "text-danger" : "text-ink-muted"}>{text.errors(logs.errors)}</span>
            </span>
          )}
        </div>
      )}
    </Card>
  );
}

/** Where the candidates line leads: the candidates on the persona view. */
const CANDIDATES_PATH = `${memoryPathOf("persona")}#${CANDIDATES_ANCHOR}`;

/**
 * What waits for the next consolidation, a line for each kind there is any of; format problems in the warning colour.
 * The candidates line leads to the candidates on the persona view.
 */
function Waiting({ pending }: { readonly pending: NonNullable<StatusReport["pending"]> }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.index;
  const lines = [
    { count: pending.candidates ?? 0, words: text.candidates, warn: false, to: CANDIDATES_PATH },
    { count: pending.formatProblems ?? 0, words: text.formatProblems, warn: true, to: null },
    { count: pending.unsummarizedWeeks ?? 0, words: text.unsummarizedWeeks, warn: false, to: null },
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
          {line.to === null ? (
            line.words(line.count)
          ) : (
            <Link to={line.to} className="text-accent underline-offset-2 hover:underline">
              {line.words(line.count)}
            </Link>
          )}
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
        <IconAction type="button" label={text.button} disabled={busy}>
          <BusyIcon busy={busy} icon={RefreshCw} />
        </IconAction>
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
 * backup; while the cloud backup is not configured, a link leads to it in the settings. In the card's head, the backup
 * button opens a menu of the local and the cloud backup, the latter dimmed while it is not configured; the one chosen runs
 * at once, without a confirmation. While it runs, the button waits; the result shows as a toast, a failure as the service's
 * explanation in the card. Beside it, the restore dialog restores a backup.
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
      setFailure({
        title: target === "remote" ? text.cloudFailedTitle : text.failed,
        message: errorTextOf(error, dictionary),
      }),
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: BACKUPS_QUERY_KEY });
      void queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY });
    },
  });
  const list = backups.data;
  const latest = list?.local[0];
  const lastRemote = list?.lastRemote ?? null;

  return (
    <Card variant="flat" id="backups" aria-labelledby="backups-title" className={className}>
      <CardHeading
        id="backups-title"
        icon={Archive}
        title={dictionary.dashboard.cards.backups}
        actions={
          list === undefined ? undefined : (
            <>
              <RestoreDialog list={list} locale={locale} />
              <DropdownMenu>
                <DropdownMenuTrigger asChild>
                  <IconAction
                    type="button"
                    label={text.backUpNow}
                    variant="default"
                    size="default"
                    className="gap-xs px-sm"
                    disabled={backup.isPending}
                  >
                    <BusyIcon busy={backup.isPending} icon={HardDriveDownload} />
                    <ChevronDown aria-hidden />
                  </IconAction>
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
            </>
          )
        }
      />
      {list === undefined ? (
        backups.isError ? (
          <FailureAlert
            failure={{ title: dictionary.dashboard.unreadable, message: errorTextOf(backups.error, dictionary) }}
          />
        ) : (
          <div aria-busy="true" className="py-lg" />
        )
      ) : (
        <>
          {latest === undefined ? (
            <span className="text-lg font-semibold">{text.none}</span>
          ) : (
            <div className="grid gap-xs">
              <span className="text-lg leading-(--tk-line-height-tight) font-semibold">
                {timeText(latest.at, locale)}
              </span>
              <span className="text-xs text-ink-muted">
                {text.latest(text.kinds[latest.kind], sizeOf(latest.bytes, locale))}
              </span>
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
        </>
      )}
    </Card>
  );
}

/**
 * An address of the service, with a button that copies it. The address takes the rest of the row and the button ends it,
 * so the copy buttons of the rows line up; a state that belongs to the address goes between them.
 */
function Address({
  value,
  label,
  children,
}: {
  readonly value: string;
  readonly label: string;
  readonly children?: ReactNode;
}) {
  const dictionary = useDictionary();
  return (
    <>
      <span title={value} className="min-w-0 flex-1 truncate select-all">
        {value}
      </span>
      {children}
      <CopyButton
        value={value}
        label={dictionary.dashboard.copy(label)}
        copiedMessage={dictionary.dashboard.copied(label)}
      />
    </>
  );
}

/** The service: its version in the head, the MCP and web addresses, each with a copy button, and the extra listener. */
export function ServiceCard({ report, className }: Omit<CardProps, "locale">) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.service;
  const service = report.service;

  return (
    <Card aria-labelledby="service-title" className={className}>
      <CardHeading
        id="service-title"
        icon={Server}
        title={dictionary.dashboard.cards.service}
        badge={
          service?.version == null ? null : (
            <span title={service.version} className="truncate text-xs text-ink-muted">
              {text.version(versionText(service.version))}
            </span>
          )
        }
      />
      {service == null ? (
        <Unreadable />
      ) : (
        <dl className="grid grid-cols-[auto_minmax(0,1fr)] content-start items-center gap-x-lg gap-y-xs text-sm">
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
              <Address value={service.extraListener.address} label={text.extraListener}>
                {!service.running ? (
                  <Badge>
                    <CircleDashed aria-hidden />
                    {text.serviceNotRunning}
                  </Badge>
                ) : service.extraListener.failureReason === null ? (
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
              </Address>
            )}
          </InfoRow>
        </dl>
      )}
    </Card>
  );
}

/** Where the bindings are kept: the bindings section of the settings. */
const BINDINGS_PATH = "/settings#settings-bindings";

/** How many chips the clients card shows; past it, the last chip counts the rest. */
const SHOWN_CLIENTS = 12;

/** What a client's binding is: sound, broken, or none (it only connected); in the order the legend lists them. */
const CLIENT_STATES = ["bound", "broken", "unbound"] as const;
type ClientState = (typeof CLIENT_STATES)[number];

function stateOf(row: AgentRow): ClientState {
  return row.binding === null ? "unbound" : row.binding.problem === null ? "bound" : "broken";
}

const STATE_DOTS: Record<ClientState, string> = {
  bound: "bg-success",
  broken: "bg-danger",
  unbound: "bg-ink-muted",
};

/** The dot of a binding's state, on a chip and in the legend; what it means is in the words beside it. */
function StateDot({ state }: { readonly state: ClientState }) {
  return <span aria-hidden className={`size-(--tk-space-sm) shrink-0 rounded-full ${STATE_DOTS[state]}`} />;
}

/**
 * The clients: every binding and every client that connected, merged into one chip per agent with its logo and name, the
 * bound ones first and then those that only connected, the most recent first. A dot on the chip tells the binding's
 * state, and a legend under the chips names the states that occur; the state and when the client last connected are in
 * the chip's tooltip and read out with it. Past twelve, the last chip counts the rest and leads to the bindings in the
 * settings, as does the link in the head. With no binding, a line says what that means and a button leads there. When
 * the bindings cannot be read, the chips have no dot and a line says so, rather than showing every client as not bound.
 */
export function ClientsCard({ report, locale, className }: CardProps) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.agents;
  const bindings = report.bindings?.items;
  const rows = agentsOf(bindings ?? [], report.clients?.items ?? []);
  const shown = rows.length > SHOWN_CLIENTS ? rows.slice(0, SHOWN_CLIENTS - 1) : rows;
  const known = bindings !== undefined;
  const states = known ? CLIENT_STATES.filter((state) => rows.some((row) => stateOf(row) === state)) : [];
  const stateText: Record<ClientState, string> = { bound: text.bound, broken: text.broken, unbound: text.seenOnly };

  return (
    <Card variant="flat" aria-labelledby="clients-title" className={className}>
      <CardHeading
        id="clients-title"
        icon={Users}
        title={text.title}
        actions={
          rows.length === 0 ? undefined : (
            <IconAction asChild label={text.manage}>
              <Link to={BINDINGS_PATH}>
                <Link2 aria-hidden />
              </Link>
            </IconAction>
          )
        }
      />
      {bindings === undefined && report.clients == null ? (
        <Unreadable />
      ) : (
        <>
          {rows.length === 0 ? null : (
            <ul className="flex flex-wrap gap-sm">
              {shown.map((row) => {
                const state = stateOf(row);
                const problem = row.binding?.problem ?? null;
                const seen = seenText(row.lastSeen, locale, text);
                const detail = !known
                  ? seen
                  : `${problem === null ? stateText[state] : dictionary.dashboard.bindingProblems[problem]} · ${seen}`;
                return (
                  <li
                    key={row.key}
                    title={`${row.label}\n${detail}`}
                    className="flex min-w-0 items-center gap-sm rounded-full bg-surface-raised py-xs pr-md pl-xs text-sm"
                  >
                    <AgentLogo icon={row.icon} />
                    <span className="truncate">{row.label}</span>
                    {known ? <StateDot state={state} /> : null}
                    <span className="sr-only">{detail}</span>
                  </li>
                );
              })}
              {rows.length > shown.length ? (
                <li>
                  <Link
                    to={BINDINGS_PATH}
                    aria-label={text.moreLabel(rows.length - shown.length)}
                    className="flex h-full items-center rounded-full bg-surface-raised px-md text-sm text-accent hover:bg-accent-soft"
                  >
                    {text.more(rows.length - shown.length)}
                  </Link>
                </li>
              ) : null}
            </ul>
          )}
          {states.length === 0 ? null : (
            <ul aria-label={text.legend} className="flex flex-wrap gap-x-md gap-y-xs text-xs text-ink-muted">
              {states.map((state) => (
                <li key={state} className="flex items-center gap-xs">
                  <StateDot state={state} />
                  {stateText[state]}
                </li>
              ))}
            </ul>
          )}
          {known ? null : <p className="text-sm text-ink-muted">{text.bindingsUnreadable}</p>}
          {bindings !== undefined && bindings.length === 0 ? (
            <div className="flex flex-wrap items-center justify-between gap-sm">
              <p className="text-sm text-ink-muted">{rows.length === 0 ? text.empty : text.noBindings}</p>
              <Button asChild variant="outline">
                <Link to={BINDINGS_PATH}>
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

/** A day as the date the service gave it, in the browser's own calendar: today, yesterday, or the date. */
function seenText(
  day: string | null,
  locale: string,
  text: ReturnType<typeof useDictionary>["dashboard"]["agents"],
): string {
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
  return text.lastSeen(dayText(day, locale, { month: "short", day: "numeric" }));
}
