import { ChevronRight, TriangleAlert } from "lucide-react";
import { lazy, Suspense } from "react";
import { Link, useParams } from "react-router";
import { ApiError } from "../../api/client";
import { type PageSectionView, type PageView, usePage } from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import { useScrollToHash } from "../../app/use-scroll-to-hash";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Card } from "../../components/ui/card";
import { errorTextOf } from "../../i18n/messages";
import { sectionAnchor, TOPICS } from "./pages";
import { CodePill, FilePath, FileProblems, PageTypeBadge, WorkstreamPill } from "./parts";
import { memoryPathOf } from "./views";

const MarkdownBody = lazy(() => import("./MarkdownBody"));

/**
 * One detail page in the reading column: the trail back to the list, a head card with the page's facts, then each
 * section typeset with its short code, the day it changed and how often it was recalled. On wide screens the sections
 * are listed beside it.
 */
export function MemoryPagePage() {
  const dictionary = useDictionary();
  const text = dictionary.memory.pages;
  const { workstream = "", slug = "" } = useParams();
  const id = `${workstream}/${slug}`;
  const page = usePage(id);
  useScrollToHash(page.data !== undefined, "start");

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-page) gap-lg">
      <nav aria-label={text.trail} className="flex flex-wrap items-center gap-xs text-sm text-ink-muted">
        <Link to={memoryPathOf("pages")} className="hover:text-ink hover:underline">
          {dictionary.memory.views.pages}
        </Link>
        <ChevronRight aria-hidden className="size-(--tk-font-size-sm)" />
        <Link
          to={`${memoryPathOf("pages")}?workstream=${encodeURIComponent(workstream)}`}
          className="hover:text-ink hover:underline"
        >
          {workstream === TOPICS ? dictionary.memory.parts.topics : workstream}
        </Link>
        <ChevronRight aria-hidden className="size-(--tk-font-size-sm)" />
        <span aria-current="page" className="text-ink">
          {page.data?.title ?? slug}
        </span>
      </nav>
      {page.isError ? (
        <Alert tone={page.error instanceof ApiError && page.error.status === 404 ? "warning" : "danger"}>
          <AlertTitle>
            {page.error instanceof ApiError && page.error.status === 404 ? text.notFound : text.failed}
          </AlertTitle>
          <AlertDescription>{errorTextOf(page.error, dictionary)}</AlertDescription>
        </Alert>
      ) : page.data === undefined ? (
        <div aria-busy="true" />
      ) : (
        <PageBody page={page.data} />
      )}
    </div>
  );
}

function PageBody({ page }: { readonly page: PageView }) {
  const text = useDictionary().memory.pages;
  return (
    <div className="grid items-start gap-lg xl:grid-cols-[minmax(0,1fr)_var(--tk-size-sidebar)]">
      <div className="mx-auto grid w-full max-w-(--tk-size-prompts) gap-lg">
        <PageHead page={page} />
        {page.sections.length === 0 ? (
          <p className="text-sm text-ink-muted">{text.noSections}</p>
        ) : (
          page.sections.map((section) => <SectionCard key={section.name} section={section} />)
        )}
      </div>
      {page.sections.length === 0 ? null : (
        <nav aria-label={text.contents} className="sticky top-lg hidden gap-xs text-sm xl:grid">
          <p className="text-xs font-semibold text-ink-muted">{text.contents}</p>
          {page.sections.map((section) => (
            <a
              key={section.name}
              href={`#${encodeURIComponent(sectionAnchor(section.name))}`}
              className="truncate border-l-(length:--tk-border-thin) border-line py-xs pl-sm text-ink-muted hover:border-accent hover:text-ink"
            >
              {section.name}
            </a>
          ))}
        </nav>
      )}
    </div>
  );
}

/** The page's head: title, type, aliases, dates, workstream, how often it was recalled, its related pages and its file. */
function PageHead({ page }: { readonly page: PageView }) {
  const dictionary = useDictionary();
  const text = dictionary.memory.pages;
  return (
    <Card aria-labelledby="page-title">
      <div className="grid gap-sm">
        <div className="flex flex-wrap items-center gap-sm">
          <h1 id="page-title" className="text-xl leading-(--tk-line-height-tight) font-semibold">
            {page.title}
          </h1>
          <PageTypeBadge type={page.type} />
        </div>
        {page.aliases.length === 0 ? null : <p className="text-sm text-ink-muted">{page.aliases.join(" · ")}</p>}
        <div className="flex flex-wrap items-center gap-x-md gap-y-xs text-xs text-ink-muted">
          <WorkstreamPill name={page.workstream} />
          {page.first === null ? null : (
            <span className="tabular-nums">{text.dates(page.first, page.last ?? page.first)}</span>
          )}
          <span className="tabular-nums">{dictionary.memory.parts.uses(page.uses)}</span>
        </div>
        {page.related.length === 0 ? null : (
          <div className="flex flex-wrap items-center gap-x-sm gap-y-xs text-sm">
            <span className="text-xs text-ink-muted">{text.related}</span>
            {page.related.map((related) =>
              related.exists ? (
                <Link
                  key={related.id}
                  to={`${memoryPathOf("pages")}/${related.id}`}
                  className="font-mono text-xs text-accent underline-offset-2 hover:underline"
                >
                  {related.id}
                </Link>
              ) : (
                <span key={related.id} className="font-mono text-xs text-ink-muted">
                  {related.id} <span className="font-sans text-warning">· {text.missing}</span>
                </span>
              ),
            )}
          </div>
        )}
        <FilePath path={page.file.path} />
      </div>
      <FileProblems problems={page.file.problems} />
    </Card>
  );
}

/** One section: its name with its short code, the day it changed and how often it was recalled, then its text typeset. */
function SectionCard({ section }: { readonly section: PageSectionView }) {
  const parts = useDictionary().memory.parts;
  const text = useDictionary().memory.pages;
  return (
    <Card
      id={sectionAnchor(section.name)}
      aria-labelledby={`${sectionAnchor(section.name)}-title`}
      className="scroll-mt-lg"
    >
      <div className="flex flex-wrap items-center gap-x-sm gap-y-xs">
        <h2
          id={`${sectionAnchor(section.name)}-title`}
          className="text-lg leading-(--tk-line-height-tight) font-semibold"
        >
          {section.name}
        </h2>
        <CodePill code={section.code} />
        <span className="ml-auto flex gap-md text-xs text-ink-muted tabular-nums">
          {section.changed === null ? null : <span>{parts.changed(section.changed)}</span>}
          <span>{parts.uses(section.uses)}</span>
        </span>
      </div>
      {section.body.trim() === "" ? (
        <p className="flex items-center gap-xs text-sm text-ink-muted">
          <TriangleAlert aria-hidden className="size-(--tk-font-size-sm)" />
          {text.emptySection}
        </p>
      ) : (
        <Suspense fallback={<div aria-busy="true" />}>
          <MarkdownBody text={section.body} />
        </Suspense>
      )}
    </Card>
  );
}
