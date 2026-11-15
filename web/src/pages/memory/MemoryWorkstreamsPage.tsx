import { cn } from "cn";
import { ArrowRight, ChevronLeft, FolderGit2, MousePointerClick } from "lucide-react";
import { Link, useParams } from "react-router";
import { useActivity } from "../../api/activity";
import { ApiError } from "../../api/client";
import {
  type BriefPage,
  type BriefPageGroup,
  useWorkstreamBrief,
  useWorkstreams,
  type WorkstreamBriefView,
  type WorkstreamListItem,
} from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Badge } from "../../components/ui/badge";
import { Card, CardTitle } from "../../components/ui/card";
import { errorTextOf } from "../../i18n/messages";
import { type ActivityReport, HEAT_LEVEL_CLASS } from "../dashboard/heatmap";
import { CodePill, FilePath, FileProblems, PageTypeBadge } from "./parts";
import { SectionGroup } from "./PersonaSections";
import { memoryPathOf } from "./views";
import { MINI_WEEKS, weekLevel, workstreamWeeks } from "./workstreams";

/** The address of one workstream's brief. */
function briefPathOf(name: string): string {
  return `${memoryPathOf("workstreams")}/${encodeURIComponent(name)}`;
}

/** The address of one detail page. */
function pagePathOf(id: string): string {
  return `${memoryPathOf("pages")}/${id}`;
}

/**
 * The workstreams view of the memory page, a list beside its detail. On wide screens the list sits on the left and the
 * chosen workstream's brief on the right; on narrow ones they are two addresses, the list alone and the brief alone
 * with a way back.
 */
export function MemoryWorkstreamsPage() {
  const dictionary = useDictionary();
  const name = useParams().name ?? null;

  return (
    <div className="mx-auto grid w-full items-start gap-lg xl:grid-cols-[var(--tk-size-master)_minmax(0,1fr)]">
      <h1 className="sr-only">{dictionary.memory.views.workstreams}</h1>
      <div className={cn("xl:sticky xl:top-lg", name === null ? "grid" : "hidden xl:grid")}>
        <WorkstreamList selected={name} />
      </div>
      {name === null ? (
        <p className="hidden items-center gap-sm pt-md text-sm text-ink-muted xl:flex">
          <MousePointerClick aria-hidden className="size-(--tk-font-size-base)" />
          {dictionary.memory.workstreams.pick}
        </p>
      ) : (
        <Brief key={name} name={name} />
      )}
    </div>
  );
}

/** The list: the established workstreams in file order, then the names in use that are not, fainter. */
function WorkstreamList({ selected }: { readonly selected: string | null }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.workstreams;
  const list = useWorkstreams();
  const activity = useActivity();

  if (list.isError) {
    return (
      <Alert tone="danger">
        <AlertTitle>{text.failed}</AlertTitle>
        <AlertDescription>{errorTextOf(list.error, dictionary)}</AlertDescription>
      </Alert>
    );
  }
  if (list.data === undefined) {
    return <div aria-busy="true" />;
  }
  return (
    <Card aria-label={text.listLabel}>
      {list.data.established.length === 0 && list.data.unestablished.length === 0 ? (
        <p className="text-sm text-ink-muted">{text.none}</p>
      ) : null}
      {list.data.established.length === 0 ? null : (
        <ListGroup
          title={text.established}
          items={list.data.established}
          selected={selected}
          activity={activity.data}
        />
      )}
      {list.data.unestablished.length === 0 ? null : (
        <ListGroup
          title={text.unestablished}
          items={list.data.unestablished}
          selected={selected}
          activity={activity.data}
          faint
        />
      )}
      <div className="grid gap-sm border-t-(length:--tk-border-thin) border-line pt-md">
        <FilePath path={list.data.file.path} />
        <FileProblems problems={list.data.file.problems} />
      </div>
    </Card>
  );
}

function ListGroup({
  title,
  items,
  selected,
  activity,
  faint = false,
}: {
  readonly title: string;
  readonly items: readonly WorkstreamListItem[];
  readonly selected: string | null;
  readonly activity: ActivityReport | undefined;
  readonly faint?: boolean;
}) {
  return (
    <section
      aria-label={title}
      className="grid gap-xs border-t-(length:--tk-border-thin) border-line pt-md first-of-type:border-t-0 first-of-type:pt-0"
    >
      <h2 className="px-sm text-xs font-semibold text-ink-muted">{title}</h2>
      <ul className="grid gap-xs">
        {items.map((item) => (
          <ListRow key={item.name} item={item} current={item.name === selected} activity={activity} faint={faint} />
        ))}
      </ul>
    </section>
  );
}

/** A workstream in the list: its name, background and recent phase, its mini activity bar, page count and last activity. */
function ListRow({
  item,
  current,
  activity,
  faint,
}: {
  readonly item: WorkstreamListItem;
  readonly current: boolean;
  readonly activity: ActivityReport | undefined;
  readonly faint: boolean;
}) {
  const text = useDictionary().memory.workstreams;
  return (
    <li>
      <Link
        to={briefPathOf(item.name)}
        aria-current={current ? "page" : undefined}
        className={cn(
          "grid gap-xs rounded-md px-sm py-sm outline-none hover:bg-surface-raised focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
          current && "bg-accent-soft hover:bg-accent-soft",
        )}
      >
        <span
          className={cn(
            "flex items-center gap-sm font-medium break-all",
            current ? "text-accent" : faint && "text-ink-muted",
          )}
        >
          <FolderGit2 aria-hidden className="size-(--tk-font-size-base) shrink-0" />
          {item.name}
        </span>
        {item.background === null ? null : (
          <span className="line-clamp-2 text-sm text-ink-muted">{item.background}</span>
        )}
        {item.recent === null ? null : <span className="line-clamp-2 text-xs text-ink-muted">{item.recent}</span>}
        {activity === undefined ? null : <MiniBar report={activity} workstream={item.name} onAccent={current} />}
        <span className="flex flex-wrap gap-x-sm text-xs text-ink-muted tabular-nums">
          <span>{text.pages(item.pages)}</span>
          {item.lastActivity === null ? null : <span>{text.lastActivity(item.lastActivity)}</span>}
        </span>
      </Link>
    </li>
  );
}

/**
 * A workstream's activities per week over about half a year, one cell a week, on the dashboard heatmap's colours. On the
 * chosen row's accent background an empty week takes the surface colour, so it still shows.
 */
function MiniBar({
  report,
  workstream,
  onAccent,
}: {
  readonly report: ActivityReport;
  readonly workstream: string;
  readonly onAccent: boolean;
}) {
  const text = useDictionary().memory.workstreams;
  const weeks = workstreamWeeks(report, workstream);
  return (
    <span
      role="img"
      aria-label={text.weeks(MINI_WEEKS)}
      className="grid gap-xs"
      style={{ gridTemplateColumns: `repeat(${MINI_WEEKS}, minmax(0, 1fr))` }}
    >
      {weeks.map((week, index) => (
        <span
          key={week.monday}
          title={text.week(week.monday, week.count)}
          style={{ gridColumn: MINI_WEEKS - weeks.length + index + 1 }}
          className={cn(
            "h-sm rounded-xs",
            onAccent && week.count === 0 ? "bg-surface" : HEAT_LEVEL_CLASS[weekLevel(week.count)],
          )}
        />
      ))}
    </span>
  );
}

/** The chosen workstream's brief, whole; on narrow screens with a way back to the list. */
function Brief({ name }: { readonly name: string }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.workstreams;
  const brief = useWorkstreamBrief(name);
  const missing = brief.error instanceof ApiError && brief.error.status === 404;

  return (
    <div className="grid min-w-0 gap-lg">
      <Link
        to={memoryPathOf("workstreams")}
        className="flex items-center gap-xs text-sm text-ink-muted hover:text-ink hover:underline xl:hidden"
      >
        <ChevronLeft aria-hidden className="size-(--tk-font-size-sm)" />
        {text.back}
      </Link>
      {brief.isError ? (
        <Alert tone={missing ? "warning" : "danger"}>
          <AlertTitle>{missing ? text.notFound : text.briefFailed}</AlertTitle>
          <AlertDescription>{errorTextOf(brief.error, dictionary)}</AlertDescription>
        </Alert>
      ) : brief.data === undefined ? (
        <div aria-busy="true" />
      ) : (
        <BriefBody brief={brief.data} />
      )}
    </div>
  );
}

function BriefBody({ brief }: { readonly brief: WorkstreamBriefView }) {
  const text = useDictionary().memory.workstreams;
  return (
    <>
      <Card aria-labelledby="brief-name">
        <div className="flex flex-wrap items-center gap-sm">
          <FolderGit2 aria-hidden className="size-(--tk-font-size-lg) text-ink-muted" />
          <CardTitle id="brief-name">{brief.name}</CardTitle>
          {brief.established ? null : <Badge>{text.notEstablished}</Badge>}
        </div>
        {brief.established ? (
          <dl className="grid gap-sm text-sm sm:grid-cols-[auto_minmax(0,1fr)] sm:gap-x-md">
            <dt className="text-ink-muted">{text.background}</dt>
            <dd className="leading-(--tk-line-height-normal)">{brief.background ?? text.empty}</dd>
            <dt className="text-ink-muted">{text.recent}</dt>
            <dd className="leading-(--tk-line-height-normal)">{brief.recent ?? text.empty}</dd>
          </dl>
        ) : (
          <p className="text-sm text-ink-muted">{text.unestablishedHint}</p>
        )}
      </Card>

      {brief.conventions.length === 0 ? null : (
        <Card aria-labelledby="brief-conventions">
          <CardTitle id="brief-conventions">{text.conventions}</CardTitle>
          {brief.conventions.map((section, index) => (
            <SectionGroup key={`${section.section ?? "other"}-${index}`} section={section} problems={[]} />
          ))}
        </Card>
      )}

      {brief.openItems.length === 0 ? null : (
        <Card aria-labelledby="brief-open">
          <CardTitle id="brief-open">{text.openItems}</CardTitle>
          <ul className="grid gap-sm">
            {brief.openItems.map((item) => (
              <li key={item.page} className="grid gap-xs rounded-md bg-warning-soft px-md py-sm">
                <Link
                  to={pagePathOf(item.page)}
                  className="w-fit font-mono text-xs text-ink-muted hover:text-ink hover:underline"
                >
                  {item.page}
                </Link>
                {item.lines.map((line, index) => (
                  <p key={index} className="text-sm leading-(--tk-line-height-normal)">
                    {line}
                  </p>
                ))}
              </li>
            ))}
          </ul>
        </Card>
      )}

      {brief.groups.length === 0 && brief.overturned.length === 0 ? null : (
        <Card aria-labelledby="brief-directory">
          <CardTitle id="brief-directory">{text.directory}</CardTitle>
          {brief.groups.map((group) => (
            <PageGroup key={group.type ?? "untyped"} group={group} />
          ))}
          {brief.overturned.length === 0 ? null : (
            <p className="flex flex-wrap items-baseline gap-x-sm gap-y-xs border-t-(length:--tk-border-thin) border-line pt-md text-sm text-ink-muted">
              <span>{text.overturned}</span>
              {brief.overturned.map((id) => (
                <Link key={id} to={pagePathOf(id)} className="font-mono text-xs line-through hover:text-ink">
                  {id}
                </Link>
              ))}
            </p>
          )}
        </Card>
      )}

      <Card aria-labelledby="brief-activities">
        <div className="flex flex-wrap items-baseline justify-between gap-sm">
          <CardTitle id="brief-activities">{text.activities}</CardTitle>
          <Link
            to={`${memoryPathOf("activity")}?workstream=${encodeURIComponent(brief.name)}`}
            className="flex items-center gap-xs text-sm text-accent hover:underline"
          >
            {text.viewAll}
            <ArrowRight aria-hidden className="size-(--tk-font-size-sm)" />
          </Link>
        </div>
        {brief.activities.length === 0 ? (
          <p className="text-sm text-ink-muted">{text.empty}</p>
        ) : (
          <ul className="grid gap-sm">
            {brief.activities.map((activity) => (
              <li key={activity.code} className="grid grid-cols-[minmax(0,1fr)_auto] items-start gap-x-md gap-y-xs">
                <div className="grid min-w-0 gap-xs">
                  <p className="leading-(--tk-line-height-normal)">{activity.text}</p>
                  <p className="text-xs text-ink-muted tabular-nums">{activity.date}</p>
                </div>
                <CodePill code={activity.code} />
              </li>
            ))}
          </ul>
        )}
        {brief.summary === null ? null : (
          <section
            aria-label={text.summary(brief.summary.label)}
            className="grid gap-xs border-t-(length:--tk-border-thin) border-line pt-md"
          >
            <h3 className="flex flex-wrap items-baseline gap-x-sm text-sm font-semibold">
              {text.summary(brief.summary.label)}
              <span className="text-xs font-regular text-ink-muted tabular-nums">
                {`${brief.summary.first} – ${brief.summary.last}`}
              </span>
            </h3>
            <p className="text-sm leading-(--tk-line-height-normal) whitespace-pre-line">{brief.summary.text}</p>
          </section>
        )}
      </Card>
    </>
  );
}

/** The pages of one type, newest first: each with its title, aliases, the day it changed and how often it was recalled. */
function PageGroup({ group }: { readonly group: BriefPageGroup }) {
  return (
    <section className="grid gap-xs border-t-(length:--tk-border-thin) border-line pt-md first-of-type:border-t-0 first-of-type:pt-0">
      <h3>
        <PageTypeBadge type={group.type} />
      </h3>
      <ul className="grid">
        {group.pages.map((page) => (
          <PageRow key={page.id} page={page} />
        ))}
      </ul>
    </section>
  );
}

function PageRow({ page }: { readonly page: BriefPage }) {
  const dictionary = useDictionary();
  const parts = dictionary.memory.parts;
  const text = dictionary.memory.workstreams;
  return (
    <li>
      <Link
        to={pagePathOf(page.id)}
        className="grid gap-xs rounded-md px-sm py-sm outline-none hover:bg-surface-raised focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent md:grid-cols-[minmax(0,1fr)_auto] md:items-center md:gap-md"
      >
        <span className="grid min-w-0 gap-xs">
          <span className="font-medium break-words">{page.title}</span>
          {page.aliases.length === 0 ? null : (
            <span className="truncate text-xs text-ink-muted">{page.aliases.join(" · ")}</span>
          )}
        </span>
        <span className="flex flex-wrap items-center gap-x-sm text-xs text-ink-muted tabular-nums">
          {page.changed === null ? null : <span>{parts.changed(page.changed)}</span>}
          <span title={page.lastUse === null ? undefined : text.lastUse(page.lastUse)}>{parts.uses(page.uses)}</span>
        </span>
      </Link>
    </li>
  );
}
