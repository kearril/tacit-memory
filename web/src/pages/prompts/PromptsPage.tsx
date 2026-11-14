import { BookOpenText, ListChecks, type LucideIcon, PenLine, Pin, RefreshCw } from "lucide-react";
import { Navigate, useParams } from "react-router";
import type { PromptDocument, PromptName } from "../../api/prompts";
import { usePrompts } from "../../api/prompts";
import { useDictionary } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import { errorTextOf } from "../../i18n/messages";
import { BusyIcon } from "../dashboard/parts";
import { DEFAULT_PROMPT, FILE_NAMES, promptNameOf, promptPathOf, RESIDENT_SECTIONS } from "./names";

/** The icon of each document, in the head of its card. */
const PROMPT_ICONS: Readonly<Record<PromptName, LucideIcon>> = {
  instructions: BookOpenText,
  rules: ListChecks,
  resident: Pin,
  skill: PenLine,
};

/**
 * The prompts page: one document at a time, the one its address names, as a card with what the document is for and its
 * text. The list of the four documents sits in the sidebar under the page's name; an address that names none goes on to
 * the core rules. The page heading is for screen readers only, as the sidebar already shows which page this is.
 */
export function PromptsPage() {
  const dictionary = useDictionary();
  const name = promptNameOf(useParams().name);
  const prompts = usePrompts();
  if (name === undefined) {
    return <Navigate to={promptPathOf(DEFAULT_PROMPT)} replace />;
  }

  const document = prompts.data?.documents.find((candidate) => candidate.name === name);
  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-settings) gap-xl">
      <h1 className="sr-only">{dictionary.nav.prompts}</h1>
      {document !== undefined ? (
        <PromptCard key={name} document={document} />
      ) : prompts.isError ? (
        <Alert tone="danger">
          <AlertTitle>{dictionary.prompts.loadFailed}</AlertTitle>
          <AlertDescription className="grid gap-sm">
            <p>{errorTextOf(prompts.error, dictionary)}</p>
            <div>
              <Button type="button" variant="outline" disabled={prompts.isFetching} onClick={() => void prompts.refetch()}>
                <BusyIcon busy={prompts.isFetching} icon={RefreshCw} />
                {dictionary.settings.readAgain}
              </Button>
            </div>
          </AlertDescription>
        </Alert>
      ) : (
        <div aria-busy="true" className="min-h-dvh" />
      )}
    </div>
  );
}

/**
 * One document as a card: its icon, its name, its file and whether the built-in text applies; a few lines on where the
 * text goes and when a change takes effect; then the text itself.
 */
function PromptCard({ document }: { readonly document: PromptDocument }) {
  const dictionary = useDictionary();
  const text = dictionary.prompts;
  const about = text.documents[document.name];
  const Icon = PROMPT_ICONS[document.name];
  const titleId = `prompt-${document.name}-title`;

  return (
    <section aria-labelledby={titleId} className="grid gap-lg rounded-lg bg-surface p-lg shadow-rest">
      <header className="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-x-sm gap-y-xs">
        <span className="grid size-(--tk-size-icon-tile) place-items-center rounded-sm bg-accent-soft text-accent">
          <Icon aria-hidden className="size-(--tk-font-size-sm)" />
        </span>
        <div className="flex flex-wrap items-center gap-sm">
          <h2 id={titleId} className="text-lg font-semibold">
            {about.title}
          </h2>
          <code className="font-mono text-sm text-ink-muted">{FILE_NAMES[document.name]}</code>
          {document.builtIn ? <Badge>{text.builtIn}</Badge> : <Badge tone="accent">{text.customized}</Badge>}
        </div>
        <div className="col-start-2 grid gap-xs text-sm text-ink-muted">
          {about.help.map((line) => (
            <p key={line}>{line}</p>
          ))}
          {document.name === "resident" ? (
            <p className="flex flex-wrap items-center gap-xs">
              {text.residentSections}
              {RESIDENT_SECTIONS.map((section) => (
                <code key={section} className="rounded-sm bg-surface-raised px-xs font-mono text-xs text-ink">
                  ## {section}
                </code>
              ))}
            </p>
          ) : null}
        </div>
      </header>

      <p className="text-sm text-ink-muted">{document.builtIn ? text.builtInNote : text.customizedNote(FILE_NAMES[document.name])}</p>
      <textarea
        readOnly
        aria-label={text.textLabel(about.title)}
        value={document.text}
        placeholder={text.empty}
        spellCheck={false}
        className="min-h-(--tk-size-editor) w-full resize-y rounded-md border-(length:--tk-border-thin) border-line bg-surface-raised p-md font-mono text-sm leading-(--tk-line-height-normal) text-ink outline-none placeholder:text-ink-muted focus-visible:border-accent focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
      />
    </section>
  );
}
