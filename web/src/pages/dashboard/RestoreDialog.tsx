import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { CircleCheck, Cloud, History, LoaderCircle, RotateCcw, TriangleAlert } from "lucide-react";
import { useState } from "react";
import {
  RESTORE_AFFECTS,
  type BackupList,
  previewRestore,
  type RestorePreview,
  type RestoreResult,
  type RestoreSource,
  runRestore,
} from "../../api/backups";
import { afterCommand } from "../../api/effects";
import { useDictionary } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "../../components/ui/dialog";
import { errorTextOf } from "../../i18n/messages";
import { fileNameOf, sizeOf, timeText, versionText } from "../../lib/format";
import { BusyIcon } from "../../components/ui/busy-icon";
import { IconAction, InfoRow } from "./parts";

/** The backup chosen in the list: a local one by its full path, or the latest cloud backup. */
type Choice = { readonly kind: "local"; readonly path: string } | { readonly kind: "cloud" };

function sourceOf(choice: Choice): RestoreSource {
  return choice.kind === "cloud" ? { remote: true } : { path: choice.path };
}

/** The value that tells the choices apart in the list and in the preview's query key. */
function keyOf(choice: Choice): string {
  return choice.kind === "cloud" ? "cloud" : `local:${choice.path}`;
}

/**
 * The restore, from the backup card: the local backups and, when one is configured, the latest cloud backup, to choose
 * from. Choosing one previews it (a cloud backup is downloaded first), and the option to take `config.yaml` and `prompts/`
 * as well previews again. The confirm button names the action; it restores the file the preview named. While the restore
 * runs, the dialog cannot be closed. Then the dialog shows what was done and what the user has to act on.
 */
export function RestoreDialog({ list, locale }: { readonly list: BackupList; readonly locale: string }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.restore;
  const [open, setOpen] = useState(false);
  const [choice, setChoice] = useState<Choice | null>(null);
  const [config, setConfig] = useState(false);
  const queryClient = useQueryClient();

  const preview = useQuery({
    queryKey: ["restore-preview", choice === null ? null : keyOf(choice), config],
    queryFn: () => previewRestore(sourceOf(choice!), config),
    enabled: open && choice !== null,
    staleTime: Infinity,
    gcTime: 0,
    retry: false,
    refetchOnWindowFocus: false,
  });
  const restore = useMutation({
    mutationFn: (path: string) => runRestore(path, config),
    onSettled: () => afterCommand(queryClient, RESTORE_AFFECTS),
  });

  const onOpenChange = (next: boolean) => {
    if (restore.isPending) {
      return;
    }
    setOpen(next);
    if (next) {
      const newest = list.local[0];
      setChoice(
        newest !== undefined ? { kind: "local", path: newest.path } : list.remoteConfigured ? { kind: "cloud" } : null,
      );
      setConfig(false);
      restore.reset();
    }
  };

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogTrigger asChild>
        <IconAction type="button" label={text.open}>
          <History aria-hidden />
        </IconAction>
      </DialogTrigger>
      <DialogContent locked={restore.isPending}>
        {restore.data === undefined ? (
          <>
            <DialogHeader>
              <DialogTitle>{text.title}</DialogTitle>
              <DialogDescription>{text.description}</DialogDescription>
            </DialogHeader>
            <BackupChoices
              list={list}
              locale={locale}
              choice={choice}
              disabled={restore.isPending}
              onChoose={setChoice}
            />
            {choice === null ? null : (
              <PreviewPanel
                preview={preview.data}
                loading={preview.isFetching}
                cloud={choice.kind === "cloud"}
                error={preview.isError ? errorTextOf(preview.error, dictionary) : null}
                locale={locale}
              />
            )}
            <label className="flex items-center gap-sm text-sm">
              <input
                type="checkbox"
                checked={config}
                disabled={restore.isPending}
                onChange={(event) => setConfig(event.target.checked)}
                className="size-(--tk-font-size-base) accent-accent"
              />
              {text.withConfig}
            </label>
            {restore.isError ? (
              <Alert tone="danger">
                <AlertTitle>{text.failed}</AlertTitle>
                <AlertDescription>{errorTextOf(restore.error, dictionary)}</AlertDescription>
              </Alert>
            ) : null}
            <DialogFooter>
              <DialogClose asChild>
                <Button type="button" variant="ghost" disabled={restore.isPending}>
                  {text.cancel}
                </Button>
              </DialogClose>
              <Button
                type="button"
                variant="destructive"
                disabled={preview.data === undefined || preview.isFetching || restore.isPending}
                onClick={() => restore.mutate(preview.data!.path)}
              >
                <BusyIcon busy={restore.isPending} icon={RotateCcw} />
                {text.confirm}
              </Button>
            </DialogFooter>
          </>
        ) : (
          <RestoreDone result={restore.data} />
        )}
      </DialogContent>
    </Dialog>
  );
}

/** The backups to choose from, as a list of radio buttons that scrolls within the dialog. */
function BackupChoices({
  list,
  locale,
  choice,
  disabled,
  onChoose,
}: {
  readonly list: BackupList;
  readonly locale: string;
  readonly choice: Choice | null;
  readonly disabled: boolean;
  readonly onChoose: (choice: Choice) => void;
}) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.restore;
  const kinds = dictionary.dashboard.backups.kinds;
  const choices = [
    ...(list.remoteConfigured
      ? [{ choice: { kind: "cloud" } as Choice, label: text.cloudLatest, detail: null as string | null, cloud: true }]
      : []),
    ...list.local.map((backup) => ({
      choice: { kind: "local", path: backup.path } as Choice,
      label: timeText(backup.at, locale),
      detail: `${kinds[backup.kind]} · ${sizeOf(backup.bytes, locale)}`,
      cloud: false,
    })),
  ];

  if (choices.length === 0) {
    return <p className="text-sm text-ink-muted">{text.empty}</p>;
  }
  return (
    <fieldset
      disabled={disabled}
      className="grid max-h-(--tk-size-list) overflow-y-auto rounded-md border-(length:--tk-border-thin) border-line"
    >
      <legend className="sr-only">{text.title}</legend>
      {choices.map((item) => {
        const key = keyOf(item.choice);
        const chosen = choice !== null && keyOf(choice) === key;
        return (
          <label
            key={key}
            className={`flex cursor-pointer items-center gap-sm border-b-(length:--tk-border-thin) border-line px-md py-sm text-sm last:border-b-0 ${
              chosen ? "bg-accent-soft" : "hover:bg-surface-raised"
            }`}
          >
            <input
              type="radio"
              name="restore-backup"
              value={key}
              checked={chosen}
              onChange={() => onChoose(item.choice)}
              className="size-(--tk-font-size-sm) accent-accent"
            />
            {item.cloud ? <Cloud aria-hidden className="size-(--tk-font-size-sm) text-ink-muted" /> : null}
            <span className="flex-1 truncate font-medium">{item.label}</span>
            {item.detail === null ? null : <span className="text-xs text-ink-muted">{item.detail}</span>}
          </label>
        );
      })}
    </fieldset>
  );
}

/** What the chosen backup holds and what restoring it would replace, or why it cannot be read. */
function PreviewPanel({
  preview,
  loading,
  cloud,
  error,
  locale,
}: {
  readonly preview: RestorePreview | undefined;
  readonly loading: boolean;
  readonly cloud: boolean;
  readonly error: string | null;
  readonly locale: string;
}) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.restore;

  if (error !== null) {
    return (
      <Alert tone="danger">
        <AlertTitle>{text.previewFailed}</AlertTitle>
        <AlertDescription>{error}</AlertDescription>
      </Alert>
    );
  }
  if (preview === undefined || loading) {
    return (
      <p aria-busy="true" className="flex items-center gap-sm text-sm text-ink-muted">
        <LoaderCircle aria-hidden className="size-(--tk-font-size-sm) motion-safe:animate-spin" />
        {cloud ? text.downloading : text.reading}
      </p>
    );
  }
  return (
    <div className="grid gap-sm">
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] gap-x-lg gap-y-xs text-sm">
        {preview.generatedAt === null ? null : (
          <InfoRow label={text.at}>
            <span className="truncate">{timeText(preview.generatedAt, locale)}</span>
          </InfoRow>
        )}
        {preview.tacitVersion === null ? null : (
          <InfoRow label={text.version}>
            <span title={preview.tacitVersion} className="truncate">
              {versionText(preview.tacitVersion)}
            </span>
          </InfoRow>
        )}
        <InfoRow label={text.commit}>
          <span title={preview.memoryHeadSubject ?? undefined} className="truncate">
            <code className="text-xs">{preview.memoryHead.slice(0, 7)}</code> {preview.memoryHeadSubject}
          </span>
        </InfoRow>
        <InfoRow label={text.files}>
          <span>{preview.fileCount}</span>
        </InfoRow>
      </dl>
      <div className="grid gap-xs rounded-md bg-warning-soft px-md py-sm text-sm">
        <span className="flex items-center gap-xs font-medium text-warning">
          <TriangleAlert aria-hidden className="size-(--tk-font-size-sm)" />
          {text.replaces}
        </span>
        <ul className="grid gap-xs text-xs break-all">
          {preview.replaces.map((line) => (
            <li key={line}>{line.trim()}</li>
          ))}
        </ul>
      </div>
    </div>
  );
}

/** What the restore did: where the state before it was kept, the index rebuilt, and each notice to act on. */
function RestoreDone({ result }: { readonly result: RestoreResult }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.restore;
  return (
    <>
      <DialogHeader>
        <DialogTitle className="flex items-center gap-sm">
          <CircleCheck aria-hidden className="size-(--tk-font-size-lg) text-success" />
          {text.doneTitle}
        </DialogTitle>
        <DialogDescription>{text.safety(fileNameOf(result.safetyBackup))}</DialogDescription>
      </DialogHeader>
      <p className="flex flex-wrap items-center gap-sm text-sm">
        <Badge tone="success">
          <CircleCheck aria-hidden />
          {text.reindexed}
        </Badge>
        <span className="text-ink-muted">
          {dictionary.dashboard.reindex.done(result.reindex.files, result.reindex.units)}
        </span>
      </p>
      {result.notices.map((notice) => (
        <Alert key={notice} tone="warning">
          <AlertDescription className="whitespace-pre-line">{notice}</AlertDescription>
        </Alert>
      ))}
      <DialogFooter>
        <DialogClose asChild>
          <Button type="button">{text.done}</Button>
        </DialogClose>
      </DialogFooter>
    </>
  );
}
