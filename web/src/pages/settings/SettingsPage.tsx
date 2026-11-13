import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useMemo, useRef, useState } from "react";
import { useBlocker } from "react-router";
import { ApiError } from "../../api/client";
import { CONFIG_QUERY_KEY, type ConfigProblem, type ConfigView, saveConfig, useConfig } from "../../api/config";
import { STATUS_QUERY_KEY } from "../../api/status";
import { useDictionary } from "../../app/session-context";
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
import { useToast } from "../../components/ui/toast-context";
import { errorTextOf } from "../../i18n/messages";
import { AccessSection } from "./AccessSection";
import type { SettingsForm } from "./fields";
import {
  bodyOf,
  changedPaths,
  type Drafts,
  draftsOf,
  edit,
  FIELDS,
  fieldsAt,
  inputIdOf,
  placeProblems,
  restoreDefault,
  sectionIdOf,
  type SectionId,
} from "./form";
import { JudgmentCard } from "./JudgmentCard";

/** The sections that have a card so far, in the page's order. */
const SECTIONS: readonly SectionId[] = ["judgment", "access"];

/**
 * The settings page: a row of section tabs that stays at the top, then the section cards in a bento grid, and a bar that
 * rises from the bottom once anything is changed, to save the whole page at once or discard it. The page heading is for
 * screen readers only, as the sidebar already shows which page this is.
 */
export function SettingsPage() {
  const dictionary = useDictionary();
  const config = useConfig();

  return (
    <div className="grid gap-lg">
      <h1 className="sr-only">{dictionary.nav.settings}</h1>
      {config.data !== undefined ? (
        // A new hash is a new file: after a save or a reload the inputs start again from it.
        <SettingsEditor key={config.data.hash} view={config.data} onReload={() => void config.refetch()} />
      ) : config.isError ? (
        <Alert tone="danger">
          <AlertTitle>{dictionary.settings.loadFailed}</AlertTitle>
          <AlertDescription>{errorTextOf(config.error, dictionary)}</AlertDescription>
        </Alert>
      ) : (
        <div aria-busy="true" className="min-h-dvh" />
      )}
    </div>
  );
}

interface Failure {
  readonly message: string;
  readonly reload: boolean;
}

function SettingsEditor({ view, onReload }: { readonly view: ConfigView; readonly onReload: () => void }) {
  const dictionary = useDictionary();
  const text = dictionary.settings;
  const queryClient = useQueryClient();
  const showToast = useToast();
  const [drafts, setDrafts] = useState<Drafts>(() => draftsOf(view));
  const [problems, setProblems] = useState<readonly ConfigProblem[]>([]);
  const [failure, setFailure] = useState<Failure | null>(null);

  const changed = useMemo(() => changedPaths(view, drafts), [view, drafts]);
  const placed = useMemo(() => placeProblems(problems), [problems]);
  const dirty = changed.length > 0;

  const save = useMutation({
    mutationFn: () => saveConfig(view.hash, bodyOf(view, drafts).config),
    onSuccess: async () => {
      showToast(text.saved);
      void queryClient.invalidateQueries({ queryKey: STATUS_QUERY_KEY });
      await queryClient.invalidateQueries({ queryKey: CONFIG_QUERY_KEY });
    },
    onError: (error) => {
      if (error instanceof ApiError && error.problems.length > 0) {
        show(error.problems);
        setFailure(null);
      } else {
        setFailure({
          message: errorTextOf(error, dictionary),
          reload: error instanceof ApiError && error.error === "ConfigChanged",
        });
      }
    },
  });

  const show = (found: readonly ConfigProblem[]) => {
    setProblems(found);
    // The inputs are on the page whether or not they have problems, so the first one can be reached at once.
    const first = placeProblems(found).first;
    const input = first === null ? null : document.getElementById(inputIdOf(first));
    input?.scrollIntoView({ block: "center", behavior: "smooth" });
    input?.focus({ preventScroll: true });
  };

  const onSave = () => {
    setFailure(null);
    const { invalid } = bodyOf(view, drafts);
    if (invalid.length > 0) {
      show(
        invalid.map((path) => ({
          path,
          message: FIELDS.find((field) => field.path === path)?.integer ? text.notAWholeNumber : text.notANumber,
        })),
      );
      return;
    }
    setProblems([]);
    save.mutate();
  };

  const onDiscard = () => {
    setDrafts(draftsOf(view));
    setProblems([]);
    setFailure(null);
  };

  const form: SettingsForm = {
    view,
    drafts,
    changed: new Set(changed),
    problems: placed,
    edit: (path, value) => {
      setDrafts((current) => edit(current, path, value));
      setProblems((current) => current.filter((problem) => !touches(problem.path, path)));
    },
    restore: (path) => {
      setDrafts((current) => restoreDefault(view, current, path));
      setProblems((current) => current.filter((problem) => !touches(problem.path, path)));
    },
  };

  return (
    <>
      <SectionTabs counts={placed.bySection} />
      <div className="grid gap-lg lg:grid-cols-2">
        <JudgmentCard form={form} className="lg:col-span-2" />
        <AccessSection id={sectionIdOf("access")} className="scroll-mt-xl" />
      </div>
      {dirty ? (
        <SaveBar
          count={changed.length}
          saving={save.isPending}
          unplaced={placed.unplaced}
          failure={failure}
          onSave={onSave}
          onDiscard={onDiscard}
          onReload={onReload}
        />
      ) : null}
      <LeaveGuard dirty={dirty && !save.isSuccess} />
    </>
  );
}

/** Whether a problem at `problem` is about the setting or pair at `path`, so an edit there clears it. */
function touches(problem: string, path: string): boolean {
  return fieldsAt(path).some((field) => field.path === problem || field.group === problem);
}

/**
 * The row of section names at the top: each scrolls to its card, and a section with problems shows how many. It stays at
 * the top of a wide screen while the page scrolls under it.
 */
function SectionTabs({ counts }: { readonly counts: Partial<Record<SectionId, number>> }) {
  const dictionary = useDictionary();
  const text = dictionary.settings;
  return (
    <nav
      aria-label={text.sectionsNav}
      className="-mx-sm flex flex-wrap gap-xs bg-canvas/(--tk-mix-hover) px-sm py-sm backdrop-blur-sm lg:sticky lg:top-0 lg:z-(--tk-layer-nav)"
    >
      {SECTIONS.map((section) => {
        const count = counts[section] ?? 0;
        return (
          <Button
            key={section}
            type="button"
            variant="outline"
            className="rounded-full"
            onClick={() => document.getElementById(sectionIdOf(section))?.scrollIntoView({ behavior: "smooth" })}
          >
            {text.sections[section]}
            {count > 0 ? (
              <Badge tone="danger" aria-label={text.problemsIn(count)}>
                {count}
              </Badge>
            ) : null}
          </Button>
        );
      })}
    </nav>
  );
}

/**
 * The bar at the bottom while anything is changed: how many changes, discard (after a confirmation) and save. Problems no
 * field answers to, and a failed save, show above it.
 */
function SaveBar({
  count,
  saving,
  unplaced,
  failure,
  onSave,
  onDiscard,
  onReload,
}: {
  readonly count: number;
  readonly saving: boolean;
  readonly unplaced: readonly ConfigProblem[];
  readonly failure: Failure | null;
  readonly onSave: () => void;
  readonly onDiscard: () => void;
  readonly onReload: () => void;
}) {
  const dictionary = useDictionary();
  const text = dictionary.settings;
  return (
    <div className="sticky bottom-md z-(--tk-layer-nav) grid gap-sm">
      {unplaced.length > 0 ? (
        <Alert tone="danger" className="shadow-raised">
          <AlertTitle>{text.unplacedTitle}</AlertTitle>
          <ul className="col-start-2 grid gap-xs">
            {unplaced.map((problem, index) => (
              <li key={index}>
                <code className="font-mono">{problem.path}</code>：{problem.message}
              </li>
            ))}
          </ul>
        </Alert>
      ) : null}
      {failure === null ? null : (
        <Alert tone="danger" className="shadow-raised">
          <AlertTitle>{text.saveFailed}</AlertTitle>
          <AlertDescription>{failure.message}</AlertDescription>
          {failure.reload ? (
            <div className="col-start-2 pt-xs">
              <Button type="button" variant="outline" onClick={onReload}>
                {text.reload}
              </Button>
            </div>
          ) : null}
        </Alert>
      )}
      <div
        role="region"
        aria-label={text.changes(count)}
        className="flex flex-wrap items-center justify-between gap-md rounded-lg bg-surface px-lg py-md shadow-raised"
      >
        <p aria-live="polite" className="text-sm font-medium">
          {text.changes(count)}
        </p>
        <div className="flex gap-sm">
          <AlertDialog>
            <AlertDialogTrigger asChild>
              <Button type="button" variant="outline" disabled={saving}>
                {text.discard}
              </Button>
            </AlertDialogTrigger>
            <AlertDialogContent>
              <AlertDialogHeader>
                <AlertDialogTitle>{text.discardTitle}</AlertDialogTitle>
                <AlertDialogDescription>{text.discardBody}</AlertDialogDescription>
              </AlertDialogHeader>
              <AlertDialogFooter>
                <AlertDialogCancel>{text.cancel}</AlertDialogCancel>
                <AlertDialogAction variant="destructive" onClick={onDiscard}>
                  {text.discardConfirm}
                </AlertDialogAction>
              </AlertDialogFooter>
            </AlertDialogContent>
          </AlertDialog>
          <Button type="button" disabled={saving} onClick={onSave}>
            {saving ? text.saving : text.save}
          </Button>
        </div>
      </div>
    </div>
  );
}

/**
 * Keeps unsaved changes from being lost: a move to another page of the interface asks first, and closing or reloading the
 * tab is met with the browser's own question.
 */
function LeaveGuard({ dirty }: { readonly dirty: boolean }) {
  const dictionary = useDictionary();
  const text = dictionary.settings;
  // Leaving closes the dialog too; that close must not reset the navigation it just let through.
  const leaving = useRef(false);
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) => dirty && currentLocation.pathname !== nextLocation.pathname,
  );

  useEffect(() => {
    if (!dirty) {
      return;
    }
    const onBeforeUnload = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", onBeforeUnload);
    return () => window.removeEventListener("beforeunload", onBeforeUnload);
  }, [dirty]);

  return (
    <AlertDialog open={blocker.state === "blocked"} onOpenChange={(open) => {
        if (!open && !leaving.current) {
          blocker.reset?.();
        }
      }}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{text.leaveTitle}</AlertDialogTitle>
          <AlertDialogDescription>{text.leaveBody}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{text.cancel}</AlertDialogCancel>
          <AlertDialogAction variant="destructive" onClick={() => {
              leaving.current = true;
              blocker.proceed?.();
            }}>
            {text.leaveConfirm}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
