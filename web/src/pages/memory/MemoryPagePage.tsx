import { cn } from "cn";
import { ChevronRight, Link2, TriangleAlert } from "lucide-react";
import { lazy, Suspense } from "react";
import { Link, useParams } from "react-router";
import { type PageListItem, type PageSectionView, type PageView, usePage, usePages } from "../../api/memory";
import { QueryResult } from "../../components/query-result";
import { useDictionary } from "../../app/session-context";
import { useScrollToHash } from "../../app/use-scroll-to-hash";
import { BELOW_BARS } from "../../components/ui/sticky";
import { StickyBar } from "../../components/ui/sticky-bar";
import { Card, CardTitle } from "../../components/ui/card";
import { chipVariants } from "../../components/ui/chip-variants";
import { entryBox } from "../../components/ui/entry-box";
import { TOPICS } from "./pages";
import { CodePill, FilePath, FileProblems, PageTypeBadge, WorkstreamPill } from "./parts";
import { memoryPathOf, pagePathOf, pagesPathOf, sectionAnchor } from "../../lib/routes";

const MarkdownBody = lazy(() => import("./MarkdownBody"));

/**
 * One detail page in a reading column: the trail back to the list, a head card with the page's facts, a sticky row of
 * its sections to jump to, then the sections numbered in dashed boxes, each typeset under its name, the day it changed
 * and how often it was recalled. The related pages follow by title, and the page's file closes it.
 */
export function MemoryPagePage() {
  const dictionary = useDictionary();
  const text = dictionary.memory.pages;
  const { workstream = "", slug = "" } = useParams();
  const id = `${workstream}/${slug}`;
  const page = usePage(id);
  useScrollToHash(page.data !== undefined, "start");

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-prompts) grid-cols-1 gap-md">
      <nav aria-label={text.trail} className="flex flex-wrap items-center gap-xs text-sm text-ink-muted">
        <Link to={memoryPathOf("pages")} className="hover:text-ink hover:underline">
          {dictionary.memory.views.pages}
        </Link>
        <ChevronRight aria-hidden className="size-(--tk-font-size-sm)" />
        <Link to={pagesPathOf({ workstream })} className="hover:text-ink hover:underline">
          {workstream === TOPICS ? dictionary.memory.parts.topics : workstream}
        </Link>
        <ChevronRight aria-hidden className="size-(--tk-font-size-sm)" />
        <span aria-current="page" className="min-w-0 truncate text-ink">
          {page.data?.title ?? slug}
        </span>
      </nav>
      <QueryResult query={page} title={text.failed} missing={text.notFound}>
        {(data) => <PageBody page={data} />}
      </QueryResult>
    </div>
  );
}

function PageBody({ page }: { readonly page: PageView }) {
  const text = useDictionary().memory.pages;
  return (
    <>
      <PageHead page={page} />
      {page.sections.length === 0 ? (
        <p className="text-sm text-ink-muted">{text.noSections}</p>
      ) : (
        <>
          <SectionTabs sections={page.sections} />
          <Card aria-label={text.contents}>
            <ol className="grid gap-md">
              {page.sections.map((section, index) => (
                <Section key={section.name} section={section} number={index + 1} />
              ))}
            </ol>
          </Card>
        </>
      )}
      <Related page={page} />
      <footer className="grid gap-xs px-md text-xs text-ink-muted">
        <span>{text.sourceFile}</span>
        <FilePath path={page.file.path} />
        <FileProblems problems={page.file.problems} />
      </footer>
    </>
  );
}

/** The page's head: its type and title, its aliases, then its workstream, dates, uses and how many sections it has. */
function PageHead({ page }: { readonly page: PageView }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.pages;
  return (
    <Card variant="tinted" aria-labelledby="page-title">
      <div className="grid gap-sm">
        <div className="flex flex-wrap items-center gap-sm">
          <PageTypeBadge type={page.type} written={page.writtenType} />
          <h1 id="page-title" className="text-xl leading-(--tk-line-height-tight) font-semibold">
            {page.title}
          </h1>
        </div>
        {page.aliases.length === 0 ? null : <p className="text-sm text-ink-muted">{page.aliases.join(" · ")}</p>}
        <p className="flex flex-wrap items-center gap-x-lg gap-y-xs text-sm text-ink-muted tabular-nums">
          <WorkstreamPill name={page.workstream} />
          {page.first === null ? null : <span>{text.dates(page.first, page.last ?? page.first)}</span>}
          <span>{dictionary.memory.parts.uses(page.uses)}</span>
          <span>{text.sectionCount(page.sections.length)}</span>
        </p>
      </div>
    </Card>
  );
}

/** The sections' names in a sticky bar, wrapping when they do not fit in a row, each jumping to its section. */
function SectionTabs({ sections }: { readonly sections: readonly PageSectionView[] }) {
  const text = useDictionary().memory.pages;
  return (
    <StickyBar role="navigation" aria-label={text.contents}>
      <ul className="-m-xs flex flex-wrap gap-sm p-xs">
        {sections.map((section) => (
          <li key={section.name} className="shrink-0">
            <a href={`#${encodeURIComponent(sectionAnchor(section.name))}`} className={chipVariants()}>
              {section.name}
            </a>
          </li>
        ))}
      </ul>
    </StickyBar>
  );
}

/**
 * One section in a dashed box: its number and name, the day it changed and how often it was recalled, its short code on
 * hover or focus, then its text typeset.
 */
function Section({ section, number }: { readonly section: PageSectionView; readonly number: number }) {
  const parts = useDictionary().memory.parts;
  const text = useDictionary().memory.pages;
  const anchor = sectionAnchor(section.name);
  return (
    <li
      id={anchor}
      aria-labelledby={`${anchor}-title`}
      className={cn(entryBox(), BELOW_BARS, "group grid gap-sm py-md")}
    >
      <div className="flex flex-wrap items-center gap-sm">
        <span
          aria-hidden
          className="grid size-(--tk-font-size-xl) shrink-0 place-items-center rounded-full bg-accent-soft text-xs font-semibold text-accent tabular-nums"
        >
          {number}
        </span>
        <h2 id={`${anchor}-title`} className="leading-(--tk-line-height-tight) font-semibold">
          {section.name}
        </h2>
        <span className="ml-auto flex items-center gap-sm text-xs text-ink-muted tabular-nums">
          {section.changed === null ? null : <span>{parts.changed(section.changed)}</span>}
          <span>{parts.uses(section.uses)}</span>
          <CodePill code={section.code} reveal />
        </span>
      </div>
      {section.body.trim() === "" ? (
        <p className="flex items-center gap-xs text-sm text-ink-muted">
          <TriangleAlert aria-hidden className="size-(--tk-font-size-sm)" />
          {text.emptySection}
        </p>
      ) : (
        <Suspense fallback={<div aria-busy="true" />}>
          <MarkdownBody text={section.body} sources={section.sources} />
        </Suspense>
      )}
    </li>
  );
}

/**
 * The related pages by title, each with its type and workstream, read from the list of pages; one that does not exist
 * keeps its identifier in a dashed box with a warning.
 */
function Related({ page }: { readonly page: PageView }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.pages;
  const list = usePages();
  if (page.related.length === 0) {
    return null;
  }
  const items = new Map<string, PageListItem>((list.data?.pages ?? []).map((item) => [item.id, item]));
  return (
    <Card variant="flat" aria-labelledby="page-related">
      <div className="flex items-center gap-sm">
        <Link2 aria-hidden className="size-(--tk-font-size-lg) shrink-0 text-ink-muted" />
        <CardTitle id="page-related">{text.related}</CardTitle>
      </div>
      <ul className="grid gap-sm md:grid-cols-2">
        {page.related.map((related) => {
          const item = items.get(related.id);
          return (
            <li key={related.id} className="grid">
              {related.exists ? (
                <Link
                  to={pagePathOf(related.id)}
                  className="grid content-start gap-xs rounded-md bg-surface-raised px-md py-sm outline-none hover:ring-(length:--tk-border-thin) hover:ring-accent focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
                >
                  <span className="flex flex-wrap items-center gap-sm text-xs text-ink-muted">
                    <PageTypeBadge type={item?.type ?? null} written={item?.writtenType} />
                    {item === undefined
                      ? null
                      : item.workstream === TOPICS
                        ? dictionary.memory.parts.topics
                        : item.workstream}
                  </span>
                  <span className="text-sm font-medium">{item?.title ?? related.id}</span>
                </Link>
              ) : (
                <span className={cn(entryBox({ tone: "warning" }), "grid gap-xs")}>
                  <span className="font-mono text-xs text-ink-muted">{related.id}</span>
                  <span className="flex items-center gap-xs text-xs text-warning">
                    <TriangleAlert aria-hidden className="size-(--tk-font-size-xs)" />
                    {text.missing}
                  </span>
                </span>
              )}
            </li>
          );
        })}
      </ul>
    </Card>
  );
}
