import { cn } from "cn";
import {
  ArrowRight,
  FolderGit2,
  History,
  ListChecks,
  MousePointerClick,
  ScrollText,
  Sparkles,
  TriangleAlert,
} from "lucide-react";
import { useState } from "react";
import { Link, useParams } from "react-router";
import { useActivity } from "../../api/activity";
import {
  type BriefPageGroup,
  useWorkstreamBrief,
  useWorkstreams,
  type WorkstreamBriefView,
  type WorkstreamList,
  type WorkstreamListItem,
} from "../../api/memory";
import { QueryResult } from "../../components/query-result";
import { useDictionary } from "../../app/session-context";
import { StickyBar } from "../../components/ui/sticky-bar";
import { Badge } from "../../components/ui/badge";
import { Card, CardTitle } from "../../components/ui/card";
import { chipVariants } from "../../components/ui/chip-variants";
import { entryBox } from "../../components/ui/entry-box";
import { HEAT_LEVEL_CLASS } from "../../lib/heatmap";
import { CodePill, FilePath, FileProblems, PageTypeBadge } from "./parts";
import { PersonaEntryCard, SectionChip } from "./PersonaSections";
import { type DirectorySort, directoryRows, MINI_WEEKS, openItemText, weekLevel, workstreamWeeks } from "./workstreams";
import { activityPathOf, pagePathOf, workstreamNameOf, workstreamPathOf } from "../../lib/routes";

/**
 * The workstreams view of the memory page, in a reading column. A sticky strip above holds every workstream as a small
 * card (name, pages, last activity, weekly bar) that folds to a pill once the strip sticks; the chosen workstream's
 * brief follows, and the path of `workstreams.md` closes the page.
 */
export function MemoryWorkstreamsPage() {
  const dictionary = useDictionary();
  const text = dictionary.memory.workstreams;
  const name = workstreamNameOf(useParams());
  const list = useWorkstreams();

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-prompts) grid-cols-1 gap-lg">
      <h1 className="sr-only">{dictionary.memory.views.workstreams}</h1>
      <QueryResult query={list} title={text.failed}>
        {(data) => (
          <>
            <WorkstreamStrip list={data} selected={name} />
            {name === null ? (
              <p className="flex items-center gap-sm text-sm text-ink-muted">
                <MousePointerClick aria-hidden className="size-(--tk-font-size-base)" />
                {data.established.length === 0 && data.unestablished.length === 0 ? text.none : text.pick}
              </p>
            ) : (
              <Brief
                key={name}
                name={name}
                item={[...data.established, ...data.unestablished].find((item) => item.name === name)}
              />
            )}
            <footer className="grid gap-xs px-md text-xs text-ink-muted">
              <span>{text.sourceFile}</span>
              <FilePath path={data.file.path} />
              <FileProblems problems={data.file.problems} />
            </footer>
          </>
        )}
      </QueryResult>
    </div>
  );
}

/**
 * Every workstream in a strip, the established ones in file order, then the names in use that are not, dashed; the
 * cards wrap to show them all. Once the strip sticks they fold to pills, which wrap as well.
 */
function WorkstreamStrip({ list, selected }: { readonly list: WorkstreamList; readonly selected: string | null }) {
  const text = useDictionary().memory.workstreams;
  const activity = useActivity();
  const items = [
    ...list.established.map((item) => ({ item, faint: false })),
    ...list.unestablished.map((item) => ({ item, faint: true })),
  ];
  return (
    <StickyBar role="navigation" aria-label={text.listLabel}>
      {(stuck) => (
        <ul className="-m-xs flex flex-wrap gap-sm p-xs">
          {items.map(({ item, faint }) => (
            <StripItem
              key={item.name}
              item={item}
              current={item.name === selected}
              faint={faint}
              compact={stuck}
              weeks={activity.data === undefined ? null : workstreamWeeks(activity.data, item.name)}
            />
          ))}
        </ul>
      )}
    </StickyBar>
  );
}

/** A workstream in the strip: a small card with its name, pages, last activity and weekly bar, or a pill once stuck. */
function StripItem({
  item,
  current,
  faint,
  compact,
  weeks,
}: {
  readonly item: WorkstreamListItem;
  readonly current: boolean;
  readonly faint: boolean;
  readonly compact: boolean;
  readonly weeks: ReturnType<typeof workstreamWeeks> | null;
}) {
  const text = useDictionary().memory.workstreams;
  const tone = current
    ? "bg-accent-soft text-accent ring-(length:--tk-border-thick) ring-accent"
    : faint
      ? "border-(length:--tk-border-thin) border-dashed border-ink-muted/40 text-ink-muted hover:text-ink"
      : "bg-surface shadow-rest hover:shadow-hover";
  const link = "outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent transition-shadow";
  return (
    <li className="shrink-0">
      {compact ? (
        <Link
          to={workstreamPathOf(item.name)}
          aria-current={current ? "page" : undefined}
          className={chipVariants({ tone: current ? "current" : faint ? "faint" : "rest" })}
        >
          <FolderGit2 aria-hidden />
          {item.name}
        </Link>
      ) : (
        <Link
          to={workstreamPathOf(item.name)}
          aria-current={current ? "page" : undefined}
          title={item.background ?? undefined}
          className={cn("grid w-(--tk-size-sidebar) gap-xs rounded-md px-md py-sm", tone, link)}
        >
          <span className="flex min-w-0 items-center gap-xs font-medium">
            <FolderGit2 aria-hidden className="size-(--tk-font-size-base) shrink-0" />
            <span className="min-w-0 flex-1 truncate">{item.name}</span>
          </span>
          <span className="flex flex-wrap gap-x-sm text-xs text-ink-muted tabular-nums">
            <span>{text.pages(item.pages)}</span>
            {item.lastActivity === null ? null : <span>{text.lastActivity(item.lastActivity.slice(5))}</span>}
          </span>
          {weeks === null ? null : <WeekBar weeks={weeks} onAccent={current} />}
        </Link>
      )}
    </li>
  );
}

/**
 * A workstream's activities per week over about half a year, one cell a week packed tight to fit a card, on the
 * dashboard heatmap's colours. On the chosen card's accent background an empty week takes the surface colour.
 */
function WeekBar({
  weeks,
  onAccent,
}: {
  readonly weeks: ReturnType<typeof workstreamWeeks>;
  readonly onAccent: boolean;
}) {
  const text = useDictionary().memory.workstreams;
  return (
    <span
      role="img"
      aria-label={text.weeks(MINI_WEEKS)}
      className="grid gap-(--tk-border-thin)"
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

/** The chosen workstream's brief, whole. */
function Brief({ name, item }: { readonly name: string; readonly item: WorkstreamListItem | undefined }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.workstreams;
  const brief = useWorkstreamBrief(name);
  return (
    <QueryResult query={brief} title={text.briefFailed} missing={text.notFound}>
      {(data) => <BriefBody brief={data} item={item} />}
    </QueryResult>
  );
}

/** A card's head: the section's icon in the accent colour, its name, then what goes beside it. */
function CardHead({
  id,
  icon: Icon,
  title,
  quiet = false,
  children,
}: {
  readonly id: string;
  readonly icon: typeof Sparkles;
  readonly title: string;
  readonly quiet?: boolean;
  readonly children?: React.ReactNode;
}) {
  return (
    <div className="flex flex-wrap items-center gap-sm">
      <Icon aria-hidden className={`size-(--tk-font-size-lg) shrink-0 ${quiet ? "text-ink-muted" : "text-accent"}`} />
      <CardTitle id={id}>{title}</CardTitle>
      {children}
    </div>
  );
}

function BriefBody({
  brief,
  item,
}: {
  readonly brief: WorkstreamBriefView;
  readonly item: WorkstreamListItem | undefined;
}) {
  const text = useDictionary().memory.workstreams;
  const pages = brief.groups.reduce((sum, group) => sum + group.pages.length, 0);
  const lastActivity = item?.lastActivity ?? null;
  return (
    <>
      <Card variant="tinted" aria-labelledby="brief-name">
        <div className="grid gap-xs">
          <div className="flex flex-wrap items-center gap-sm">
            <FolderGit2 aria-hidden className="size-(--tk-font-size-xl) shrink-0 text-accent" />
            <h2 id="brief-name" className="text-xl leading-(--tk-line-height-tight) font-semibold break-all">
              {brief.name}
            </h2>
            {brief.established ? null : <Badge>{text.notEstablished}</Badge>}
          </div>
          {brief.established ? (
            brief.background === null ? null : (
              <p className="leading-(--tk-line-height-normal) text-ink-muted">{brief.background}</p>
            )
          ) : (
            <p className="text-sm text-ink-muted">{text.unestablishedHint}</p>
          )}
        </div>
        {brief.recent === null ? null : (
          <div className="grid gap-xs rounded-md bg-surface px-md py-sm">
            <span className="flex items-center gap-xs text-xs font-medium text-accent">
              <Sparkles aria-hidden className="size-(--tk-font-size-sm)" />
              {text.recent}
            </span>
            <p className="leading-(--tk-line-height-normal)">{brief.recent}</p>
          </div>
        )}
        <p className="flex flex-wrap gap-x-lg gap-y-xs text-sm text-ink-muted tabular-nums">
          <span>{text.pages(item?.pages ?? pages + brief.overturned.length)}</span>
          <span>{text.openCount(brief.openItems.length)}</span>
          {lastActivity === null ? null : <span>{text.lastActivity(lastActivity)}</span>}
        </p>
      </Card>

      {brief.openItems.length === 0 ? null : (
        <Card aria-labelledby="brief-open">
          <CardHead id="brief-open" icon={TriangleAlert} title={text.openItems} />
          <ul className="grid gap-sm">
            {brief.openItems.map((item) => (
              <li key={item.page} className={cn(entryBox({ tone: "warning" }), "grid gap-xs")}>
                <Link
                  to={pagePathOf(item.page)}
                  className="w-fit font-mono text-xs text-ink-muted hover:text-ink hover:underline"
                >
                  {item.page}
                </Link>
                <ul className="grid gap-xs">
                  {item.lines.map((line, index) => (
                    <li key={index} className="text-sm leading-(--tk-line-height-normal)">
                      {openItemText(line)}
                    </li>
                  ))}
                </ul>
              </li>
            ))}
          </ul>
        </Card>
      )}

      <Card variant="flat" aria-labelledby="brief-activities">
        <CardHead id="brief-activities" quiet icon={History} title={text.activities}>
          <Link
            to={activityPathOf({ workstream: brief.name })}
            className="ml-auto flex items-center gap-xs text-sm text-accent hover:underline"
          >
            {text.viewAll}
            <ArrowRight aria-hidden className="size-(--tk-font-size-sm)" />
          </Link>
        </CardHead>
        {brief.activities.length === 0 ? (
          <p className="text-sm text-ink-muted">{text.empty}</p>
        ) : (
          <ul className="grid gap-sm">
            {brief.activities.map((activity) => (
              <li key={activity.code} className={cn(entryBox(), "group grid gap-xs")}>
                <div className="flex items-center gap-sm text-xs text-ink-muted">
                  <span className="tabular-nums">{activity.date}</span>
                  <CodePill code={activity.code} reveal className="ml-auto" />
                </div>
                <p className="leading-(--tk-line-height-normal)">{activity.text}</p>
              </li>
            ))}
          </ul>
        )}
        {brief.summary === null ? null : (
          <section
            aria-label={text.summary(brief.summary.label)}
            className="grid gap-xs rounded-md bg-surface-raised px-md py-sm"
          >
            <h3 className="flex flex-wrap items-baseline gap-x-sm text-xs text-ink-muted">
              <span className="font-semibold text-ink">{text.summary(brief.summary.label)}</span>
              <span className="tabular-nums">{`${brief.summary.first} – ${brief.summary.last}`}</span>
            </h3>
            <p className="text-sm leading-(--tk-line-height-normal) whitespace-pre-line">{brief.summary.text}</p>
          </section>
        )}
      </Card>

      {brief.conventions.length === 0 ? null : (
        <Card variant="flat" aria-labelledby="brief-conventions">
          <CardHead id="brief-conventions" quiet icon={ListChecks} title={text.conventions} />
          <ul className="grid gap-sm">
            {brief.conventions.flatMap((section) =>
              section.entries.map((entry) => (
                <PersonaEntryCard
                  key={entry.line}
                  entry={entry}
                  problems={[]}
                  lead={<SectionChip section={section.section} heading={section.heading} />}
                />
              )),
            )}
          </ul>
        </Card>
      )}

      {brief.groups.length === 0 && brief.overturned.length === 0 ? null : (
        <Card aria-labelledby="brief-directory">
          <CardHead id="brief-directory" icon={ScrollText} title={text.directory}>
            <span className="text-sm text-ink-muted tabular-nums">{pages}</span>
          </CardHead>
          {brief.groups.length === 0 ? null : <Directory groups={brief.groups} />}
          {brief.overturned.length === 0 ? null : (
            <p className="flex flex-wrap items-baseline gap-x-sm gap-y-xs border-t-(length:--tk-border-thin) border-line px-sm pt-md text-xs text-ink-muted">
              <span>{text.overturned}</span>
              {brief.overturned.map((id) => (
                <Link key={id} to={pagePathOf(id)} className="font-mono line-through hover:text-ink">
                  {id}
                </Link>
              ))}
            </p>
          )}
        </Card>
      )}
    </>
  );
}

/**
 * The page directory as a table that a click on a column head sorts: by type as the brief groups it, by the day
 * changed, or by uses. Each row opens its page; its aliases show on hover. On a narrow screen only the title and uses
 * stay.
 */
function Directory({ groups }: { readonly groups: readonly BriefPageGroup[] }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.workstreams;
  const [sort, setSort] = useState<DirectorySort>("type");
  const head = (key: DirectorySort, className?: string) => (
    <button
      type="button"
      aria-pressed={sort === key}
      onClick={() => setSort(key)}
      className={cn(
        "cursor-pointer text-left outline-none hover:text-ink focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
        sort === key && "font-medium text-accent hover:text-accent",
        className,
      )}
    >
      {text.sortBy[key]}
    </button>
  );
  return (
    <div className="grid grid-cols-[minmax(0,1fr)_auto] gap-x-md md:grid-cols-[auto_minmax(0,1fr)_auto_auto]">
      <div className="col-span-2 grid grid-cols-subgrid px-sm pb-xs text-xs text-ink-muted md:col-span-4">
        {head("type", "hidden md:block")}
        <span>{text.columnTitle}</span>
        {head("changed", "hidden md:block")}
        {head("uses")}
      </div>
      <ul className="col-span-2 grid grid-cols-subgrid divide-y divide-line md:col-span-4">
        {directoryRows(groups, sort).map(({ type, page }) => (
          <li key={page.id} className="col-span-2 grid grid-cols-subgrid md:col-span-4">
            <Link
              to={pagePathOf(page.id)}
              title={page.aliases.length === 0 ? undefined : page.aliases.join(" · ")}
              className="col-span-2 grid grid-cols-subgrid items-center rounded-sm px-sm py-sm text-sm outline-none hover:bg-surface-raised focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent md:col-span-4"
            >
              <span className="hidden md:block">
                <PageTypeBadge type={type} />
              </span>
              <span className="min-w-0 truncate">{page.title}</span>
              <span className="hidden text-xs text-ink-muted tabular-nums md:block">{page.changed ?? "—"}</span>
              <span
                title={page.lastUse === null ? undefined : text.lastUse(page.lastUse)}
                className="text-right text-xs text-ink-muted tabular-nums"
              >
                {page.uses}
              </span>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
