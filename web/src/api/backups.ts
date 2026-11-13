import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "./client";
import type { components } from "./schema";

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
