import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "./client";
import type { components, paths } from "./schema";

export type BackupList = components["schemas"]["BackupList"];
export type LocalBackupKind = components["schemas"]["LocalBackupKind"];
export type BackupResult = components["schemas"]["BackupResult"];
export type BackupTarget = components["schemas"]["BackupTarget"];
export type ReindexResult = components["schemas"]["ReindexResult"];

/** The query key of the backup list. */
export const BACKUPS_QUERY_KEY = ["backups"] as const;

/** The local backups, newest first, whether the cloud backup is configured, and its last attempt; no network is touched. */
export function getBackups(): Promise<BackupList> {
  return apiFetch<BackupList>("/api/backups");
}

/** The backup list, fetched again when the window regains focus and after a backup; it changes too rarely to poll. */
export function useBackups() {
  return useQuery({ queryKey: BACKUPS_QUERY_KEY, queryFn: getBackups, refetchOnWindowFocus: true });
}

/** Makes a backup now: a manual one into `backups/`, or the cloud backup. */
export function runBackup(target: BackupTarget): Promise<BackupResult> {
  return apiFetch<BackupResult>("/api/backup", { method: "POST", body: JSON.stringify({ target }) });
}

/** Rebuilds the derived index; a maintenance operation, so it is refused while another one runs. */
export function runReindex(): Promise<ReindexResult> {
  return apiFetch<ReindexResult>("/api/reindex", { method: "POST", body: "{}" });
}

type RestoreAnswer = paths["/api/restore"]["post"]["responses"][200]["content"]["application/json"];
export type RestorePreview = components["schemas"]["RestorePreview"];
export type RestoreResult = Extract<RestoreAnswer, { safetyBackup: string }>;

/** The backup a restore takes: a local one by its full path, or the latest cloud backup. */
export type RestoreSource = { readonly path: string } | { readonly remote: true };

/**
 * What restoring `source` would do, without changing anything: the backup's time, version and commit, and what it would
 * replace (with `config.yaml` and `prompts/` when `config` is set). A cloud backup is downloaded first, and the preview's
 * `path` names the downloaded file, which is the file to restore.
 */
export function previewRestore(source: RestoreSource, config: boolean): Promise<RestorePreview> {
  return apiFetch<RestorePreview>("/api/restore", {
    method: "POST",
    body: JSON.stringify({ ...source, config, preview: true }),
  });
}

/** Restores the backup at `path`, the path a preview gave; a maintenance operation that keeps a safety backup first. */
export function runRestore(path: string, config: boolean): Promise<RestoreResult> {
  return apiFetch<RestoreResult>("/api/restore", {
    method: "POST",
    body: JSON.stringify({ path, config, preview: false }),
  });
}
