import { useQuery } from "@tanstack/react-query";
import { api, call } from "./client";
import type { components } from "./schema";

export type ActivityReport = components["schemas"]["ActivityReport"];

/** The query key of the activity counts. */
export const ACTIVITY_QUERY_KEY = ["activity"] as const;

/** The activity log's counts per day over the last 53 weeks, with the consolidation days and the skipped month files. */
export function getActivity(): Promise<ActivityReport> {
  return call(api.GET("/api/activity"));
}

/** The activity counts, fetched when the page opens and again when the window regains focus; never polled. */
export function useActivity() {
  return useQuery({ queryKey: ACTIVITY_QUERY_KEY, queryFn: getActivity, refetchOnWindowFocus: true });
}
