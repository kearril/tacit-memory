import { cn } from "cn";
import { FolderGit2, Search } from "lucide-react";
import { useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { type PageListItem, usePages } from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import { StickyBar } from "../../components/ui/sticky-bar";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Card } from "../../components/ui/card";
import { Input } from "../../components/ui/input";
import { Select } from "../../components/ui/select";
import { Chip } from "../../components/ui/chip";
import { errorTextOf } from "../../i18n/messages";
import {
  filterPages,
  PAGE_TYPES,
  type PageFilter,
  type PageSort,
  pageFilterOf,
  pageFilterParams,
  sortPages,
  TOPICS,
} from "./pages";
import { PAGE_TYPE_ICONS, PageTypeBadge } from "./parts";
import { memoryPathOf } from "./views";

/** The table's columns: type, title, workstream, changed and uses on a wide screen; the title and uses on a narrow one. */
const COLUMNS = "grid-cols-[minmax(0,1fr)_auto] md:grid-cols-[auto_minmax(0,1fr)_auto_auto_auto]";
const ROW = "col-span-2 grid grid-cols-subgrid md:col-span-5";

/**
 * The pages view of the memory page: every detail page in one table, in a reading column. A sticky bar above holds the
 * keyword, the workstream and the type as chips with their counts; all three are kept in the address, so a filtered list
 * can be bookmarked. A click on a column head orders the table by workstream, the day changed or uses; by workstream,
 * each workstream starts under its name.
 */
export function MemoryPagesPage() {
  const dictionary = useDictionary();
  const text = dictionary.memory.pages;
  const pages = usePages();
  const [search, setSearch] = useSearchParams();
  // Read once per change of the address, so the lists worked out from it below are not worked out again on every render.
  const filter = useMemo(() => pageFilterOf(search), [search]);
  const [keyword, setKeyword] = useState(filter.q);
  const [sort, setSort] = useState<PageSort>("workstream");
  const all = pages.data?.pages;
  const untyped = useMemo(
    () => (all === undefined ? [] : filterPages(all, { ...filter, type: "", q: keyword })),
    [all, filter, keyword],
  );
  const shown = useMemo(
    () => sortPages(filter.type === "" ? untyped : untyped.filter((page) => page.type === filter.type), sort),
    [untyped, filter.type, sort],
  );
  const workstreams = useMemo(
    () => [...new Set(sortPages(all ?? [], "workstream").map((page) => page.workstream))],
    [all],
  );
  const update = (change: Partial<PageFilter>) =>
    setSearch(pageFilterParams({ ...filter, q: keyword, ...change }), { replace: true });

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-prompts) grid-cols-1 gap-md">
      <h1 className="sr-only">{dictionary.memory.views.pages}</h1>
      <FilterBar>
        <div className="flex flex-wrap items-center gap-sm">
          <div className="relative min-w-0 flex-1">
            <Search
              aria-hidden
              className="pointer-events-none absolute top-1/2 left-md size-(--tk-font-size-base) -translate-y-1/2 text-ink-muted"
            />
            <Input
              aria-label={text.keyword}
              className="pl-xl"
              placeholder={text.keywordPlaceholder}
              value={keyword}
              onChange={(event) => {
                setKeyword(event.target.value);
                update({ q: event.target.value });
              }}
            />
          </div>
          <Select
            aria-label={text.workstream}
            className="w-auto max-w-full"
            value={filter.workstream}
            onValueChange={(value) => update({ workstream: value })}
            options={[
              { value: "", label: text.workstreamAll },
              ...workstreams.map((name) => ({
                value: name,
                label: name === TOPICS ? dictionary.memory.parts.topics : name,
              })),
            ]}
          />
        </div>
        <TypeChips pages={untyped} type={filter.type} onChange={(type) => update({ type })} />
      </FilterBar>

      {pages.isError ? (
        <Alert tone="danger">
          <AlertTitle>{text.failed}</AlertTitle>
          <AlertDescription>{errorTextOf(pages.error, dictionary)}</AlertDescription>
        </Alert>
      ) : all === undefined ? (
        <div aria-busy="true" />
      ) : all.length === 0 ? (
        <p className="text-sm text-ink-muted">{text.noPages}</p>
      ) : shown.length === 0 ? (
        <p className="text-sm text-ink-muted">{text.noMatch}</p>
      ) : (
        <Card aria-label={dictionary.memory.views.pages}>
          <p className="text-sm text-ink-muted">{text.total(shown.length)}</p>
          <PageTable pages={shown} sort={sort} onSort={setSort} />
        </Card>
      )}
    </div>
  );
}

/** The sticky bar the filters sit in. */
function FilterBar({ children }: { readonly children: React.ReactNode }) {
  const text = useDictionary().memory.pages;
  return (
    <StickyBar role="search" aria-label={text.filters} className="grid gap-sm">
      {children}
    </StickyBar>
  );
}

/** The type filter as chips, each with how many of the pages the other filters keep are of that type. */
function TypeChips({
  pages,
  type,
  onChange,
}: {
  readonly pages: readonly PageListItem[];
  readonly type: PageFilter["type"];
  readonly onChange: (type: PageFilter["type"]) => void;
}) {
  const dictionary = useDictionary();
  const text = dictionary.memory.pages;
  const chip = (key: PageFilter["type"]) => {
    const Icon = key === "" ? null : PAGE_TYPE_ICONS[key];
    const on = type === key;
    return (
      <Chip key={key} pressed={on} onClick={() => onChange(key)}>
        {Icon === null ? null : <Icon aria-hidden />}
        {key === "" ? text.typeAll : dictionary.memory.parts.pageTypes[key]}
        <span className="text-xs tabular-nums">
          {key === "" ? pages.length : pages.filter((page) => page.type === key).length}
        </span>
      </Chip>
    );
  };
  return (
    <div role="group" aria-label={text.type} className="flex flex-wrap gap-sm">
      {chip("")}
      {PAGE_TYPES.map(chip)}
    </div>
  );
}

/**
 * The pages as a table aligned on a subgrid, each row opening its page with its aliases shown on hover. The heads of
 * the workstream, changed and uses columns order the table, and a second click goes back to the workstream order; by
 * workstream, each one starts under its name.
 */
function PageTable({
  pages,
  sort,
  onSort,
}: {
  readonly pages: readonly PageListItem[];
  readonly sort: PageSort;
  readonly onSort: (sort: PageSort) => void;
}) {
  const dictionary = useDictionary();
  const text = dictionary.memory.pages;
  const nameOf = (workstream: string) => (workstream === TOPICS ? dictionary.memory.parts.topics : workstream);
  const head = (key: PageSort, className?: string) => (
    <button
      type="button"
      aria-pressed={sort === key}
      onClick={() => onSort(sort === key ? "workstream" : key)}
      className={cn(
        "cursor-pointer text-left outline-none hover:text-ink focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
        sort === key && "font-medium text-accent hover:text-accent",
        className,
      )}
    >
      {text.columns[key]}
    </button>
  );
  return (
    <div className={cn("grid gap-x-md", COLUMNS)}>
      <div className={cn(ROW, "border-b-(length:--tk-border-thin) border-line px-sm pb-xs text-xs text-ink-muted")}>
        <span className="hidden md:block">{text.columns.type}</span>
        <span>{text.columns.title}</span>
        {head("workstream", "hidden md:block")}
        {head("changed", "hidden md:block")}
        {head("uses", "text-right")}
      </div>
      <ul className={ROW}>
        {pages.map((page, index) => (
          <li key={page.id} className={ROW}>
            {sort === "workstream" && pages[index - 1]?.workstream !== page.workstream ? (
              <p className="col-span-full flex items-center gap-xs px-sm pt-md pb-xs text-xs font-semibold text-ink-muted">
                <FolderGit2 aria-hidden className="size-(--tk-font-size-sm)" />
                {nameOf(page.workstream)}
              </p>
            ) : null}
            <Link
              to={`${memoryPathOf("pages")}/${page.id}`}
              title={page.aliases.length === 0 ? undefined : page.aliases.join(" · ")}
              className={cn(
                ROW,
                "items-center rounded-sm px-sm py-sm text-sm outline-none hover:bg-surface-raised focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
              )}
            >
              <span className="hidden md:block">
                <PageTypeBadge type={page.type} />
              </span>
              <span className="min-w-0 truncate">{page.title}</span>
              <span className="hidden max-w-(--tk-size-setting-name) truncate text-xs text-ink-muted md:block">
                {nameOf(page.workstream)}
              </span>
              <span className="hidden text-xs text-ink-muted tabular-nums md:block">{page.changed ?? "—"}</span>
              <span className="text-right text-xs text-ink-muted tabular-nums">{page.uses}</span>
            </Link>
          </li>
        ))}
      </ul>
    </div>
  );
}
