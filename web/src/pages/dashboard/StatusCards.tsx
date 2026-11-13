import { useQueryClient } from "@tanstack/react-query";
import {
  Bot,
  CircleCheck,
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
import { STATUS_QUERY_KEY, type StatusReport } from "../../api/status";
import { useDictionary } from "../../app/session-context";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import { Card, CardTitle } from "../../components/ui/card";
import { CopyButton } from "../../components/ui/copy-button";
import { type AgentIcon, type AgentRow, agentsOf } from "./agents";
import { clockOf, durationOf, remainingOf } from "./duration";

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

/**
 * The three auxiliary models, each available, cooling down or not configured; the three look different in icon and words,
 * not only in colour, and not configured is not an error. A cooldown counts down every second from the report; when one
 * reaches zero the report is fetched again. While a model cools down, the card is outlined in the warning colour.
 */
export function ModelsCard({ report, fetchedAt, className }: Omit<CardProps, "locale"> & { readonly fetchedAt: number }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.modelStates;
  const queryClient = useQueryClient();
  const items = report.models?.items;
  const cooling = items?.some((model) => model.state === "coolingDown") ?? false;
  const now = Math.max(useNow(cooling), fetchedAt);

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
      />
      {items === undefined ? (
        <Unreadable />
      ) : (
        <ul className="grid gap-sm">
          {items.map((model, index) => (
            <li key={model.role} className="flex items-center justify-between gap-md text-sm">
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
            </li>
          ))}
        </ul>
      )}
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

/** The last consolidation, how long ago it was, and what waits for the next: candidates, format problems, weeks to summarize. */
export function IndexCard({ report, locale, className }: CardProps) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.index;
  const consolidation = report.consolidation;
  const pending = report.pending;

  return (
    <Card aria-labelledby="index-title" className={className}>
      <CardHeading id="index-title" icon={DatabaseZap} title={dictionary.dashboard.cards.index} />
      {consolidation == null ? (
        <Unreadable />
      ) : (
        <div className="grid gap-xs">
          <span className="text-xs text-ink-muted">{text.lastConsolidation}</span>
          {consolidation.lastConsolidationDay === null || consolidation.daysSince === null ? (
            <span className="text-lg font-semibold">{text.never}</span>
          ) : (
            <span className="flex items-baseline gap-sm">
              <span className="text-lg leading-(--tk-line-height-tight) font-semibold">{text.daysAgo(consolidation.daysSince)}</span>
              <span className="text-sm text-ink-muted">
                {new Date(`${consolidation.lastConsolidationDay}T00:00:00Z`).toLocaleDateString(locale, {
                  dateStyle: "medium",
                  timeZone: "UTC",
                })}
              </span>
            </span>
          )}
        </div>
      )}
      {pending == null ? null : (
        <dl className="grid grid-cols-3 gap-md border-t-(length:--tk-border-thin) border-line pt-md">
          <Figure label={text.candidates} value={pending.candidates} />
          <Figure label={text.formatProblems} value={pending.formatProblems} />
          <Figure label={text.unsummarizedWeeks} value={pending.unsummarizedWeeks ?? null} />
        </dl>
      )}
    </Card>
  );
}

/** One line of the service card: a label on the left and its value on the right, with what goes after the value. */
function ServiceRow({ label, children }: { readonly label: string; readonly children: ReactNode }) {
  return (
    <div className="contents">
      <dt className="py-xs text-ink-muted">{label}</dt>
      <dd className="flex min-w-0 items-center gap-xs">{children}</dd>
    </div>
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

/** The running service: how long it has run, its version, the MCP and web addresses, and the extra listener and whether it started. */
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
            <ServiceRow label={text.uptime}>{text.duration(durationOf(service.uptimeSeconds))}</ServiceRow>
          )}
          {service.version === null ? null : (
            <ServiceRow label={text.version}>
              <span title={service.version} className="truncate">
                {service.version.split("+")[0]}
              </span>
            </ServiceRow>
          )}
          {service.endpoint === null ? null : (
            <ServiceRow label={text.mcp}>
              <Address value={service.endpoint} label={text.mcp} />
            </ServiceRow>
          )}
          <ServiceRow label={text.web}>
            <Address value={service.webAddress} label={text.web} />
          </ServiceRow>
          <ServiceRow label={text.extraListener}>
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
          </ServiceRow>
        </dl>
      )}
    </Card>
  );
}

/** An agent's logo on a soft tile: its own colours, a one-colour shape in the colour of the text, or a plain robot. */
function AgentLogo({ icon }: { readonly icon: AgentIcon | null }) {
  return (
    <span className="grid size-(--tk-size-control) shrink-0 place-items-center rounded-md bg-surface-raised text-ink">
      {icon === null ? (
        <Bot aria-hidden className="size-(--tk-font-size-xl)" />
      ) : icon.monochrome ? (
        <span
          aria-hidden
          className="size-(--tk-font-size-xl) bg-current"
          style={{ mask: `url("${icon.url}") center / contain no-repeat` }}
        />
      ) : (
        <img alt="" src={icon.url} className="size-(--tk-font-size-xl)" />
      )}
    </span>
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
    new Date(`${day}T00:00:00Z`).toLocaleDateString(locale, { month: "short", day: "numeric", timeZone: "UTC" }),
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
                <Link to="/settings#bindings">
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
