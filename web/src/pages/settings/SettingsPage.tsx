import { useMutation, useQueryClient } from "@tanstack/react-query";
import { RefreshCw } from "lucide-react";
import { useEffect, useMemo, useRef, useState } from "react";
import { useBlocker } from "react-router";
import { ApiError } from "../../api/client";
import {
  CONFIG_QUERY_KEY,
  type ConfigProblem,
  type ConfigView,
  type RestartChange,
  saveConfig,
  useConfig,
} from "../../api/config";
import { STATUS_QUERY_KEY } from "../../api/status";
import { useScrollToHash } from "../../app/use-scroll-to-hash";
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
import { Button } from "../../components/ui/button";
import { BusyIcon } from "../dashboard/parts";
import { useToast } from "../../components/ui/toast-context";
import { errorTextOf } from "../../i18n/messages";
import type { SettingsForm } from "./fields";
import {
  bodyOf,
  changedPaths,
  type Drafts,
  draftsOf,
  drifted,
  edit,
  FIELDS,
  fieldsAt,
  inputIdOf,
  pendingRestart,
  placeProblems,
  restartsAmong,
  restoreDefault,
  type SecretEdits,
} from "./form";
import { AdvancedSection } from "./AdvancedSection";
import { BackupSection } from "./BackupSection";
import { BindingsSection } from "./BindingsSection";
import { ConsolidationSection } from "./ConsolidationSection";
import { JudgmentSection } from "./JudgmentSection";
import { ModelsSection } from "./ModelsSection";
import { RemoteAccessSection } from "./RemoteAccessSection";
import { usePublishProblems, useScrollSpy } from "./section-nav";

/**
 * The settings page: one column of sections, read from top to bottom, whose list sits in the sidebar under the page's
 * name; and a bar that rises from the bottom once anything is changed, to save the whole page at once or discard it. The
 * page heading is for screen readers only, as the sidebar already shows which page this is.
 */
export function SettingsPage() {
  const dictionary = useDictionary();
  const config = useConfig();
  // Kept here, above the editor, as a save gives the editor a new file and so starts it again.
  const [restart, setRestart] = useState<readonly RestartChange[]>([]);
  useScrollToHash(config.data !== undefined, "start");
  useScrollSpy(config.data !== undefined);

  return (
    <div className="mx-auto grid w-full max-w-(--tk-size-settings) gap-xl">
      <h1 className="sr-only">{dictionary.nav.settings}</h1>
      <RestartDialog changes={restart} onClose={() => setRestart([])} />
      {config.data !== undefined ? (
        // A new hash is a new file: after a save or a reload the inputs start again from it.
        <SettingsEditor
          key={config.data.hash}
          view={config.data}
          onReload={() => void config.refetch()}
          onRestart={setRestart}
        />
      ) : config.isError ? (
        <LoadFailed error={config.error} retrying={config.isFetching} onRetry={() => void config.refetch()} />
      ) : (
        <div aria-busy="true" className="min-h-dvh" />
      )}
    </div>
  );
}

/**
 * What the page shows when it cannot read the settings. A `config.yaml` broken by hand, or restored broken from a backup,
 * names each key that is wrong, says the service runs on with the config it last loaded, and offers to read the file again
 * once it is fixed; any other failure gives the service's explanation.
 */
function LoadFailed({ error, retrying, onRetry }: { readonly error: Error; readonly retrying: boolean; readonly onRetry: () => void }) {
  const dictionary = useDictionary();
  const text = dictionary.settings;
  const invalid = error instanceof ApiError && error.error === "ConfigInvalid";

  return (
    <Alert tone="danger">
      <AlertTitle>{invalid ? text.fileInvalid : text.loadFailed}</AlertTitle>
      <AlertDescription className="grid gap-sm">
        {invalid && error.problems.length > 0 ? (
          <ul className="grid gap-xs">
            {error.problems.map((problem) => (
              <li key={problem.path} className="flex flex-wrap gap-x-sm">
                <code className="font-mono">{problem.path}</code>
                <span>{problem.message}</span>
              </li>
            ))}
          </ul>
        ) : (
          <p>{errorTextOf(error, dictionary)}</p>
        )}
        {invalid ? <p>{text.fileInvalidNote}</p> : null}
        <div>
          <Button type="button" variant="outline" disabled={retrying} onClick={onRetry}>
            <BusyIcon busy={retrying} icon={RefreshCw} />
            {text.readAgain}
          </Button>
        </div>
      </AlertDescription>
    </Alert>
  );
}

interface Failure {
  readonly message: string;
  readonly reload: boolean;
}

function SettingsEditor({
  view,
  onReload,
  onRestart,
}: {
  readonly view: ConfigView;
  readonly onReload: () => void;
  readonly onRestart: (changes: readonly RestartChange[]) => void;
}) {
  const dictionary = useDictionary();
  const text = dictionary.settings;
  const queryClient = useQueryClient();
  const showToast = useToast();
  const [drafts, setDrafts] = useState<Drafts>(() => draftsOf(view));
  const [problems, setProblems] = useState<readonly ConfigProblem[]>([]);
  const [secrets, setSecrets] = useState<SecretEdits>({});
  const [failure, setFailure] = useState<Failure | null>(null);

  const changed = useMemo(() => changedPaths(view, drafts, secrets), [view, drafts, secrets]);
  const placed = useMemo(() => placeProblems(problems), [problems]);
  const pending = useMemo(() => new Map(pendingRestart(view).map((item) => [item.path, item])), [view]);
  const drift = useMemo(() => drifted(view), [view]);
  const dirty = changed.length > 0;
  usePublishProblems(placed.bySection);

  const save = useMutation({
    mutationFn: async () => {
      const body = bodyOf(view, drafts, secrets);
      const saved = await saveConfig(view.hash, body.config, body.secrets);
      // The service lists every setting still waiting for a restart; the dialog is about the ones this save changed, as the
      // others are already marked on the page.
      return saved.restart.filter((change) => changed.includes(change.path));
    },
    onSuccess: async (restart) => {
      // A save that leaves settings waiting for a restart says so in a dialog, which also says the rest is in effect.
      if (restart.length > 0) {
        onRestart(restart);
      } else {
        showToast(text.saved);
      }
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
    // A model with a problem opens to show it, so the first input is reached once the page has drawn the problems.
    const first = placeProblems(found).first;
    window.setTimeout(() => {
      const input = first === null ? null : document.getElementById(inputIdOf(first));
      input?.scrollIntoView({ block: "center", behavior: "smooth" });
      input?.focus({ preventScroll: true });
    });
  };

  const onSave = () => {
    setFailure(null);
    const { invalid } = bodyOf(view, drafts, secrets);
    if (invalid.length > 0) {
      show(invalid.map((path) => ({ path, message: invalidText(path) })));
      return;
    }
    setProblems([]);
    save.mutate();
  };

  const invalidText = (path: string) => {
    const kind = FIELDS.find((field) => field.path === path)?.kind;
    return kind === undefined ? text.secret.emptyRewrite : kind === "integer" ? text.notAWholeNumber : text.notANumber;
  };

  const onDiscard = () => {
    setDrafts(draftsOf(view));
    setSecrets({});
    setProblems([]);
    setFailure(null);
  };

  const form: SettingsForm = {
    view,
    drafts,
    changed: new Set(changed),
    problems: placed,
    pending,
    edit: (path, value) => {
      setDrafts((current) => edit(current, path, value));
      setProblems((current) => current.filter((problem) => !touches(problem.path, path)));
    },
    restore: (path) => {
      setDrafts((current) => restoreDefault(view, current, path));
      setProblems((current) => current.filter((problem) => !touches(problem.path, path)));
    },
    secrets,
    editSecret: (path, value) => {
      setSecrets((current) => {
        const rest = Object.fromEntries(Object.entries(current).filter(([key]) => key !== path));
        return value === undefined ? rest : { ...rest, [path]: value };
      });
      setProblems((current) => current.filter((problem) => problem.path !== path));
    },
  };

  return (
    <>
      {drift.length === 0 ? null : (
        <Alert tone="warning">
          <AlertTitle>{text.driftTitle}</AlertTitle>
          <AlertDescription>
            {text.driftBody}{" "}
            {drift.map((path, index) => (
              <span key={path}>
                {index === 0 ? null : ", "}
                <code className="font-mono">{path}</code>
              </span>
            ))}
          </AlertDescription>
        </Alert>
      )}
      <ModelsSection form={form} />
      <BindingsSection />
      <JudgmentSection form={form} />
      <ConsolidationSection form={form} />
      <BackupSection form={form} />
      <RemoteAccessSection form={form} />
      <AdvancedSection form={form} />
      {dirty ? (
        <SaveBar
          count={changed.length}
          restarts={restartsAmong(view, changed)}
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
 * The bar at the bottom while anything is changed: how many changes and how many of them wait for a restart, discard
 * (after a confirmation) and save. Problems no field answers to, and a failed save, show above it.
 */
function SaveBar({
  count,
  restarts,
  saving,
  unplaced,
  failure,
  onSave,
  onDiscard,
  onReload,
}: {
  readonly count: number;
  readonly restarts: number;
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
              <AlertDialog>
                <AlertDialogTrigger asChild>
                  <Button type="button" variant="outline">
                    {text.reload}
                  </Button>
                </AlertDialogTrigger>
                <AlertDialogContent>
                  <AlertDialogHeader>
                    <AlertDialogTitle>{text.reloadTitle}</AlertDialogTitle>
                    <AlertDialogDescription>{text.reloadBody}</AlertDialogDescription>
                  </AlertDialogHeader>
                  <AlertDialogFooter>
                    <AlertDialogCancel>{text.cancel}</AlertDialogCancel>
                    <AlertDialogAction variant="destructive" onClick={onReload}>
                      {text.reloadConfirm}
                    </AlertDialogAction>
                  </AlertDialogFooter>
                </AlertDialogContent>
              </AlertDialog>
            </div>
          ) : null}
        </Alert>
      )}
      <div
        role="region"
        aria-label={text.changes(count)}
        className="flex flex-wrap items-center justify-between gap-md rounded-lg bg-surface px-lg py-md shadow-raised"
      >
        <p aria-live="polite" className="grid text-sm font-medium">
          {text.changes(count)}
          {restarts === 0 ? null : <span className="text-xs font-normal text-ink-muted">{text.changesRestart(restarts)}</span>}
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

/**
 * After a save that changed settings which wait for a restart: each with its running and its saved value, and that the
 * rest is in effect. It only informs, so its one button closes it.
 */
function RestartDialog({ changes, onClose }: { readonly changes: readonly RestartChange[]; readonly onClose: () => void }) {
  const dictionary = useDictionary();
  const text = dictionary.settings;
  return (
    <AlertDialog open={changes.length > 0} onOpenChange={(open) => (open ? undefined : onClose())}>
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{text.restartTitle}</AlertDialogTitle>
          <AlertDialogDescription>{text.restartBody}</AlertDialogDescription>
        </AlertDialogHeader>
        <ul className="grid gap-sm text-sm">
          {changes.map((change) => (
            <li key={change.path} className="grid gap-xs rounded-md bg-surface-raised px-md py-sm">
              <code className="font-mono text-xs text-ink-muted">{change.path}</code>
              <span className="font-mono break-all">{text.restartLine(change.running, change.saved)}</span>
            </li>
          ))}
        </ul>
        <AlertDialogFooter>
          <AlertDialogAction onClick={onClose}>{text.restartOk}</AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
