import { useMutation, useQueryClient } from "@tanstack/react-query";
import { BookOpenText, Info, ListChecks, type LucideIcon, PenLine, Pin, RefreshCw } from "lucide-react";
import { lazy, Suspense, useState } from "react";
import { Navigate, useParams } from "react-router";
import { ApiError } from "../../api/client";
import type { ConfigProblem } from "../../api/config";
import type { PromptDocument, PromptList, PromptMeasure, PromptName, PromptSaved } from "../../api/prompts";
import { PROMPTS_QUERY_KEY, restorePromptDefault, savePrompt, usePromptCheck, usePrompts } from "../../api/prompts";
import { STATUS_QUERY_KEY } from "../../api/status";
import { useDictionary } from "../../app/session-context";
import { useDebounced } from "../../app/use-debounced";
import { LeaveGuard } from "../../components/leave-guard";
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
import { Popover, PopoverContent, PopoverTrigger } from "../../components/ui/popover";
import { useToast } from "../../components/ui/toast-context";
import { Tooltip, TooltipContent, TooltipTrigger } from "../../components/ui/tooltip";
import { errorTextOf } from "../../i18n/messages";
import { BusyIcon } from "../dashboard/parts";
import { lf, stateOf } from "./editor";
import { measureLines } from "./measure";
import { DEFAULT_PROMPT, FILE_NAMES, promptNameOf, promptPathOf, RESIDENT_SECTIONS } from "./names";

/** The Markdown editor, loaded only once the prompts page opens, so the other pages do not carry it. */
const MarkdownEditor = lazy(() => import("./MarkdownEditor"));

/** How long the user pauses typing before the text is checked, in milliseconds. */
const CHECK_DELAY = 400;

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
    <div className="mx-auto grid w-full max-w-(--tk-size-prompts) gap-xl">
      <h1 className="sr-only">{dictionary.nav.prompts}</h1>
      {document !== undefined ? (
        <PromptCard key={name} document={document} onReload={() => void prompts.refetch()} />
      ) : prompts.isError ? (
        <Alert tone="danger">
          <AlertTitle>{dictionary.prompts.loadFailed}</AlertTitle>
          <AlertDescription className="grid gap-sm">
            <p>{errorTextOf(prompts.error, dictionary)}</p>
            <div>
              <Button
                type="button"
                variant="outline"
                disabled={prompts.isFetching}
                onClick={() => void prompts.refetch()}
              >
                <BusyIcon busy={prompts.isFetching} icon={RefreshCw} />
                {dictionary.prompts.readAgain}
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
 * One document as a card: its icon, its name, its file and whether the built-in text applies, with what the document is
 * for behind an information button; then the text and what can be done with it. What did not go as planned in
 * the last save stays shown above the text until the next one. A file the service does not run, as it breaks its rules,
 * is named at the top with the reason.
 */
function PromptCard({ document, onReload }: { readonly document: PromptDocument; readonly onReload: () => void }) {
  const dictionary = useDictionary();
  const text = dictionary.prompts;
  const queryClient = useQueryClient();
  const showToast = useToast();
  const [notices, setNotices] = useState<readonly string[]>([]);
  const about = text.documents[document.name];
  const Icon = PROMPT_ICONS[document.name];
  const titleId = `prompt-${document.name}-title`;
  const stateNote = document.builtIn ? text.builtInNote : text.customizedNote(FILE_NAMES[document.name]);

  const onDone = (saved: PromptSaved, message: string) => {
    queryClient.setQueryData<PromptList>(PROMPTS_QUERY_KEY, (list) =>
      list === undefined
        ? list
        : {
            documents: list.documents.map((candidate) =>
              candidate.name === saved.document.name ? saved.document : candidate,
            ),
          },
    );
    setNotices(saved.notices);
    showToast(message);
    // The core rules and the recording criteria are measured together, so the other one's length changed as well.
    void queryClient.invalidateQueries({ queryKey: PROMPTS_QUERY_KEY });
    // A save or a return to the default may fix a broken file, which the dashboard lists from the status.
    void queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY });
  };

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
          <Tooltip>
            <TooltipTrigger asChild>
              {/* Focusable, so the keyboard reaches what the badge means as well. */}
              <span
                tabIndex={0}
                className="rounded-sm outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
              >
                {document.builtIn ? <Badge>{text.builtIn}</Badge> : <Badge tone="accent">{text.customized}</Badge>}
              </span>
            </TooltipTrigger>
            <TooltipContent>{stateNote}</TooltipContent>
          </Tooltip>
          <About document={document} stateNote={stateNote} />
        </div>
      </header>

      {document.problem === null ? null : (
        <Alert tone="danger">
          <AlertTitle>{text.invalidTitle}</AlertTitle>
          <AlertDescription className="grid gap-xs">
            <p>{document.problem}</p>
            <p>{text.invalidNote}</p>
          </AlertDescription>
        </Alert>
      )}
      {notices.length === 0 ? null : <ListAlert tone="warning" title={text.noticesTitle} items={notices} />}
      {/* A new hash is a new file: after a save, a return to the default or a reload the editor starts again from it. */}
      <PromptEditor
        key={document.hash ?? "built-in"}
        document={document}
        label={text.textLabel(about.title)}
        onStart={() => setNotices([])}
        onDone={onDone}
        onReload={onReload}
      />
    </section>
  );
}

/**
 * The information button beside a document's name, and what it opens: where the text goes, its limits and when a change
 * takes effect; the sections the resident document accepts; and whether the file or the built-in text applies. It opens
 * on a click, so a touch screen and the keyboard reach it too.
 */
function About({ document, stateNote }: { readonly document: PromptDocument; readonly stateNote: string }) {
  const dictionary = useDictionary();
  const text = dictionary.prompts;
  const about = text.documents[document.name];
  return (
    <Popover>
      <PopoverTrigger asChild>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label={text.aboutLabel(about.title)}
          className="text-ink-muted"
        >
          <Info aria-hidden />
        </Button>
      </PopoverTrigger>
      <PopoverContent className="max-w-(--tk-size-content)">
        {about.help.map((line) => (
          <p key={line}>{line}</p>
        ))}
        {document.name === "resident" ? (
          <p className="flex flex-wrap items-center gap-xs">
            {text.residentSections}
            {RESIDENT_SECTIONS.map((section) => (
              <code key={section} className="rounded-sm bg-surface-raised px-xs font-mono text-xs">
                ## {section}
              </code>
            ))}
          </p>
        ) : null}
        <p className="text-ink-muted">{stateNote}</p>
      </PopoverContent>
    </Popover>
  );
}

/** An alert that lists its points one to a line under its title. */
function ListAlert({
  tone,
  title,
  items,
}: {
  readonly tone: "warning" | "danger";
  readonly title: string;
  readonly items: readonly string[];
}) {
  return (
    <Alert tone={tone}>
      <AlertTitle>{title}</AlertTitle>
      <ul className="col-start-2 grid gap-xs">
        {items.map((item, index) => (
          <li key={index}>{item}</li>
        ))}
      </ul>
    </Alert>
  );
}

/** A refused save or return to the default: the document's problems, or the explanation and whether a reload helps. */
type Failure =
  | { readonly kind: "rejected"; readonly problems: readonly ConfigProblem[] }
  | { readonly kind: "failed"; readonly title: string; readonly message: string; readonly reload: boolean };

/**
 * The text of one document and its actions. A built-in text is read-only until the user starts customizing it; then, or
 * once a file exists, it is saved as a whole, discarded, or (with a file) returned to its default by deleting the file.
 */
function PromptEditor({
  document,
  label,
  onStart,
  onDone,
  onReload,
}: {
  readonly document: PromptDocument;
  readonly label: string;
  readonly onStart: () => void;
  readonly onDone: (saved: PromptSaved, message: string) => void;
  readonly onReload: () => void;
}) {
  const dictionary = useDictionary();
  const text = dictionary.prompts;
  const file = FILE_NAMES[document.name];
  const [customizing, setCustomizing] = useState(false);
  const [draft, setDraft] = useState(document.text);
  const [failure, setFailure] = useState<Failure | null>(null);
  const state = stateOf(document, customizing, draft);
  // The text as it is saved needs no check: the document carries its measure. Any other text is checked once typing pauses.
  const settled = useDebounced(draft, CHECK_DELAY);
  const edited = lf(settled) !== lf(document.text);
  const check = usePromptCheck(document.name, settled, edited);
  const measure = !edited ? document.measure : check.isError ? undefined : check.data?.measure;
  const problems = edited && !check.isPlaceholderData ? (check.data?.problems ?? []) : [];

  const failed = (title: string) => (error: Error) => {
    if (error instanceof ApiError && error.problems.length > 0) {
      setFailure({ kind: "rejected", problems: error.problems });
    } else {
      setFailure({
        kind: "failed",
        title,
        message: errorTextOf(error, dictionary),
        reload: error instanceof ApiError && error.error === "PromptChanged",
      });
    }
  };
  const save = useMutation({
    mutationFn: () => savePrompt(document.name, document.hash, draft),
    onSuccess: (saved) => onDone(saved, text.saved),
    onError: failed(text.saveFailed),
  });
  const restore = useMutation({
    mutationFn: () => restorePromptDefault(document.name),
    onSuccess: (saved) => onDone(saved, text.restored),
    onError: failed(text.restoreFailed),
  });
  const busy = save.isPending || restore.isPending;

  const run = (action: typeof save | typeof restore) => {
    setFailure(null);
    onStart();
    action.mutate();
  };
  const discard = () => {
    setDraft(document.text);
    setCustomizing(false);
    setFailure(null);
  };

  return (
    <div className="grid gap-md">
      {/* Until the editor's code arrives, an empty frame of its least height holds its place. */}
      <Suspense
        fallback={
          <div
            aria-busy="true"
            className="min-h-(--tk-size-editor) rounded-md border-(length:--tk-border-thin) border-line"
          />
        }
      >
        <MarkdownEditor
          value={draft}
          onChange={setDraft}
          readOnly={state.readOnly}
          label={label}
          placeholder={text.empty}
          focused={customizing}
        />
      </Suspense>
      {measure === undefined ? null : <Measure measure={measure} />}

      {problems.length === 0 || failure?.kind === "rejected" ? null : (
        <ListAlert tone="warning" title={text.problemsTitle} items={problems} />
      )}
      {failure === null ? null : failure.kind === "rejected" ? (
        <ListAlert
          tone="danger"
          title={text.rejectedTitle}
          items={failure.problems.map((problem) => problem.message)}
        />
      ) : (
        <Alert tone="danger">
          <AlertTitle>{failure.title}</AlertTitle>
          <AlertDescription>{failure.message}</AlertDescription>
          {failure.reload ? (
            <div className="col-start-2 pt-xs">
              <ConfirmButton
                label={text.reload}
                title={text.reloadTitle}
                body={text.reloadBody(file)}
                confirm={text.reloadConfirm}
                onConfirm={onReload}
              />
            </div>
          ) : null}
        </Alert>
      )}

      <div className="flex flex-wrap items-center justify-between gap-sm">
        <div>
          {state.canRestore ? (
            <ConfirmButton
              label={text.restore}
              title={text.restoreTitle}
              body={text.restoreBody(file)}
              confirm={text.restoreConfirm}
              disabled={busy}
              onConfirm={() => run(restore)}
            />
          ) : null}
        </div>
        <div className="flex flex-wrap items-center gap-sm">
          {state.dirty ? (
            <p aria-live="polite" className="text-sm text-ink-muted">
              {text.unsaved}
            </p>
          ) : null}
          {state.readOnly ? (
            <Button type="button" onClick={() => setCustomizing(true)}>
              {text.customize}
            </Button>
          ) : (
            <>
              {state.dirty ? (
                <ConfirmButton
                  label={text.discard}
                  title={text.discardTitle}
                  body={text.discardBody}
                  confirm={text.discardConfirm}
                  disabled={busy}
                  onConfirm={discard}
                />
              ) : customizing ? (
                <Button type="button" variant="outline" disabled={busy} onClick={discard}>
                  {text.discard}
                </Button>
              ) : null}
              <Button type="button" disabled={!state.canSave || busy} onClick={() => run(save)}>
                {save.isPending ? text.saving : text.save}
              </Button>
            </>
          )}
        </div>
      </div>
      <LeaveGuard
        dirty={state.dirty && !save.isSuccess}
        text={{ title: text.leaveTitle, body: text.leaveBody, confirm: text.leaveConfirm, cancel: text.cancel }}
      />
    </div>
  );
}

/**
 * The measure of the text under it, one part after another: past a limit, a part turns to the danger tone, so the colour
 * is never the only sign as the numbers say it too. Announced politely, so a screen reader hears it once typing pauses.
 */
function Measure({ measure }: { readonly measure: PromptMeasure }) {
  const dictionary = useDictionary();
  const words = dictionary.prompts.measure;
  return (
    <p aria-live="polite" className="-mt-sm flex flex-wrap gap-x-md gap-y-xs text-xs text-ink-muted tabular-nums">
      {measureLines(measure, words).map((line) => (
        <span key={line.text} className={line.over ? "font-medium text-danger" : undefined}>
          {line.text}
        </span>
      ))}
    </p>
  );
}

/** An outline button whose action is destructive, so it asks first in a dialog. */
function ConfirmButton({
  label,
  title,
  body,
  confirm,
  disabled = false,
  onConfirm,
}: {
  readonly label: string;
  readonly title: string;
  readonly body: string;
  readonly confirm: string;
  readonly disabled?: boolean;
  readonly onConfirm: () => void;
}) {
  const dictionary = useDictionary();
  return (
    <AlertDialog>
      <AlertDialogTrigger asChild>
        <Button type="button" variant="outline" disabled={disabled}>
          {label}
        </Button>
      </AlertDialogTrigger>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{title}</AlertDialogTitle>
          <AlertDialogDescription>{body}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{dictionary.prompts.cancel}</AlertDialogCancel>
          <AlertDialogAction variant="destructive" onClick={onConfirm}>
            {confirm}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
