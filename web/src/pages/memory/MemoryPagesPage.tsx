import { Search } from "lucide-react";
import { useMemo, useState } from "react";
import { Link, useSearchParams } from "react-router";
import { type PageListItem, usePages } from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Card, CardTitle } from "../../components/ui/card";
import { Input } from "../../components/ui/input";
import { Label } from "../../components/ui/label";
import { NativeSelect } from "../../components/ui/native-select";
import { errorTextOf } from "../../i18n/messages";
import { PAGE_TYPES, filterPages, groupPages, pageFilterOf, pageFilterParams, TOPICS } from "./pages";
import { PageTypeBadge } from "./parts";
import { memoryPathOf } from "./views";

/**
 * The pages view of the memory page: every detail page, grouped by workstream with the topics last. The workstream and
 * the type filter in the address; the keyword filters the titles and aliases as it is typed and is written to the address
 * too, so a filtered list can be bookmarked.
 */
export function MemoryPagesPage() {
  const dictionary = useDictionary();
  const text = dictionary.memory.pages;
  const pages = usePages();
  const [search, setSearch] = useSearchParams();
  const filter = pageFilterOf(search);
  const [keyword, setKeyword] = useState(filter.q);
  const shown = useMemo(
    () => (pages.data === undefined ? [] : filterPages(pages.data.pages, { ...filter, q: keyword })),
    [pages.data, filter, keyword],
  );
  const workstreams = useMemo(
    () => [...new Set(pages.data?.pages.map((page) => page.workstream) ?? [])].sort(),
    [pages.data],
  );
  const update = (change: Partial<typeof filter>) =>
    setSearch(pageFilterParams({ ...filter, q: keyword, ...change }), { replace: true });

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-prompts) gap-lg">
      <h1 className="sr-only">{dictionary.memory.views.pages}</h1>
      <Card>
        <div className="grid gap-md md:grid-cols-[minmax(0,2fr)_minmax(0,1fr)_minmax(0,1fr)]">
          <div className="grid content-start gap-xs">
            <Label htmlFor="pages-keyword">{text.keyword}</Label>
            <div className="relative">
              <Search
                aria-hidden
                className="pointer-events-none absolute top-1/2 left-md size-(--tk-font-size-base) -translate-y-1/2 text-ink-muted"
              />
              <Input
                id="pages-keyword"
                type="search"
                className="pl-xl"
                placeholder={text.keywordPlaceholder}
                value={keyword}
                onChange={(event) => {
                  setKeyword(event.target.value);
                  update({ q: event.target.value });
                }}
              />
            </div>
          </div>
          <div className="grid content-start gap-xs">
            <Label htmlFor="pages-workstream">{text.workstream}</Label>
            <NativeSelect
              id="pages-workstream"
              value={filter.workstream}
              onChange={(event) => update({ workstream: event.target.value })}
            >
              <option value="">{text.workstreamAll}</option>
              {workstreams.map((name) => (
                <option key={name} value={name}>
                  {name === TOPICS ? dictionary.memory.parts.topics : name}
                </option>
              ))}
            </NativeSelect>
          </div>
          <div className="grid content-start gap-xs">
            <Label htmlFor="pages-type">{text.type}</Label>
            <NativeSelect
              id="pages-type"
              value={filter.type}
              onChange={(event) => update({ type: event.target.value as typeof filter.type })}
            >
              <option value="">{text.typeAll}</option>
              {PAGE_TYPES.map((type) => (
                <option key={type} value={type}>
                  {dictionary.memory.parts.pageTypes[type]}
                </option>
              ))}
            </NativeSelect>
          </div>
        </div>
      </Card>

      {pages.isError ? (
        <Alert tone="danger">
          <AlertTitle>{text.failed}</AlertTitle>
          <AlertDescription>{errorTextOf(pages.error, dictionary)}</AlertDescription>
        </Alert>
      ) : pages.data === undefined ? (
        <div aria-busy="true" />
      ) : pages.data.pages.length === 0 ? (
        <p className="text-sm text-ink-muted">{text.noPages}</p>
      ) : shown.length === 0 ? (
        <p className="text-sm text-ink-muted">{text.noMatch}</p>
      ) : (
        groupPages(shown).map((group) => (
          <Card key={group.workstream} aria-labelledby={`pages-${group.workstream}`}>
            <div className="flex items-baseline gap-sm">
              <CardTitle id={`pages-${group.workstream}`}>
                {group.workstream === TOPICS ? dictionary.memory.parts.topics : group.workstream}
              </CardTitle>
              <span className="text-xs text-ink-muted">{text.count(group.pages.length)}</span>
            </div>
            <ul className="grid">
              {group.pages.map((page) => (
                <PageRow key={page.id} page={page} />
              ))}
            </ul>
          </Card>
        ))
      )}
    </div>
  );
}

/** One page: its title and aliases, its type, the day it last changed and how often it was recalled; the row opens it. */
function PageRow({ page }: { readonly page: PageListItem }) {
  const parts = useDictionary().memory.parts;
  return (
    <li className="border-t-(length:--tk-border-thin) border-line first:border-t-0">
      <Link
        to={`${memoryPathOf("pages")}/${page.id}`}
        className="grid gap-xs rounded-md px-sm py-sm outline-none hover:bg-surface-raised focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent md:grid-cols-[minmax(0,1fr)_auto] md:items-center md:gap-md"
      >
        <span className="grid min-w-0 gap-xs">
          <span className="font-medium break-words">{page.title}</span>
          {page.aliases.length === 0 ? null : (
            <span className="truncate text-xs text-ink-muted">{page.aliases.join(" · ")}</span>
          )}
        </span>
        <span className="flex flex-wrap items-center gap-sm text-xs text-ink-muted">
          <PageTypeBadge type={page.type} />
          {page.changed === null ? null : <span className="tabular-nums">{parts.changed(page.changed)}</span>}
          <span className="tabular-nums">{parts.uses(page.uses)}</span>
        </span>
      </Link>
    </li>
  );
}
