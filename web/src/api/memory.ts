import { useQuery } from "@tanstack/react-query";
import { api, call } from "./client";
import type { components } from "./schema";

export type RecallTraceRequest = components["schemas"]["RecallTraceRequest"];
export type RecallTraceReport = components["schemas"]["RecallTraceReport"];
export type TraceUnit = components["schemas"]["TraceUnit"];
export type TraceResult = components["schemas"]["TraceResult"];
export type UnitFate = components["schemas"]["UnitFate"];
export type FullTextMethod = components["schemas"]["FullTextMethod"];
export type PersonaReport = components["schemas"]["PersonaReport"];
export type PersonaSection = components["schemas"]["PersonaSection"];
export type PersonaSectionView = components["schemas"]["PersonaSectionView"];
export type PersonaEntryView = components["schemas"]["PersonaEntryView"];
export type PersonaBlockView = components["schemas"]["PersonaBlockView"];
export type CandidateView = components["schemas"]["CandidateView"];
export type CandidateOccurrenceView = components["schemas"]["CandidateOccurrenceView"];
export type MemoryFileInfo = components["schemas"]["MemoryFileInfo"];
export type FileProblem = components["schemas"]["FileProblem"];
export type PageList = components["schemas"]["PageList"];
export type PageListItem = components["schemas"]["PageListItem"];
export type PageType = components["schemas"]["PageType"];
export type PageView = components["schemas"]["PageView"];
export type PageSectionView = components["schemas"]["PageSectionView"];
export type RecognizedSection = components["schemas"]["RecognizedSection"];
export type TraceSection = components["schemas"]["TraceSection"];
export type SourceView = components["schemas"]["SourceView"];
export type SourceCommentView = components["schemas"]["SourceCommentView"];
export type WorkstreamList = components["schemas"]["WorkstreamList"];
export type WorkstreamListItem = components["schemas"]["WorkstreamListItem"];
export type WorkstreamBriefView = components["schemas"]["WorkstreamBriefView"];
export type BriefPageGroup = components["schemas"]["BriefPageGroup"];
export type BriefPage = components["schemas"]["BriefPage"];
export type ActivityMonths = components["schemas"]["ActivityMonths"];
export type ActivityMonthView = components["schemas"]["ActivityMonthView"];
export type ActivityDayView = components["schemas"]["ActivityDayView"];
export type ActivityItemView = components["schemas"]["ActivityItemView"];
export type ActivitySummaryView = components["schemas"]["ActivitySummaryView"];

/** The prefix of every query that reads the memory, so a change to it invalidates them together. */
export const MEMORY_QUERY_KEY = ["memory"] as const;

export const RECALL_TRACE_QUERY_KEY = [...MEMORY_QUERY_KEY, "recall-trace"] as const;

/**
 * Runs a recall trace: the search `recall` would run for the same parameters, stage by stage. It counts no return and
 * changes no file. Bad parameters are refused with every problem (`RequestInvalid`).
 */
export function traceRecall(request: RecallTraceRequest): Promise<RecallTraceReport> {
  return call(api.POST("/api/recall/trace", { body: request }));
}

/**
 * The trace of `request`, run once per distinct request; `null` runs nothing. A trace is a snapshot the user asks for,
 * so it is never refetched on its own: running the same search again is an explicit refetch.
 */
export function useRecallTrace(request: RecallTraceRequest | null) {
  return useQuery({
    queryKey: [...RECALL_TRACE_QUERY_KEY, request],
    queryFn: () => traceRecall(request!),
    enabled: request !== null,
    retry: false,
    refetchOnWindowFocus: false,
    staleTime: Infinity,
  });
}

/** The query key of the persona view. */
export const PERSONA_QUERY_KEY = ["memory", "persona"] as const;

/** The persona and its candidates, with the two files' paths and problems. Reads files only; counts nothing. */
export function getPersona(): Promise<PersonaReport> {
  return call(api.GET("/api/memory/persona"));
}

/** The persona, fetched when the view opens and again when the window regains focus; never polled. */
export function usePersona() {
  return useQuery({ queryKey: PERSONA_QUERY_KEY, queryFn: getPersona, refetchOnWindowFocus: true });
}

/** The query key of the page list; one page's key adds its ID. */
export const PAGES_QUERY_KEY = ["memory", "pages"] as const;

/** Every detail page, for the list. Counts nothing. */
export function getPages(): Promise<PageList> {
  return call(api.GET("/api/memory/pages"));
}

/** One detail page with its sections. Counts nothing; a page that does not exist is refused with `MemoryNotFound`. */
export function getPage(id: string): Promise<PageView> {
  const [workstream, slug] = id.split("/");
  return call(
    api.GET("/api/memory/pages/{workstream}/{slug}", {
      params: { path: { workstream: workstream ?? "", slug: slug ?? "" } },
    }),
  );
}

/** The page list, fetched when the view opens and again when the window regains focus; never polled. */
export function usePages() {
  return useQuery({ queryKey: PAGES_QUERY_KEY, queryFn: getPages, refetchOnWindowFocus: true });
}

/** One page, fetched as the list is; a missing page is not asked for again. */
export function usePage(id: string) {
  return useQuery({
    queryKey: [...PAGES_QUERY_KEY, id],
    queryFn: () => getPage(id),
    refetchOnWindowFocus: true,
    retry: false,
  });
}

/** The query key of the workstream list; one workstream's brief adds its name. */
export const WORKSTREAMS_QUERY_KEY = ["memory", "workstreams"] as const;

/** The established workstreams and the names in use that are not. Reads files only; counts nothing. */
export function getWorkstreams(): Promise<WorkstreamList> {
  return call(api.GET("/api/memory/workstreams"));
}

/** A workstream's brief, whole. Counts nothing; a name with nothing behind it is refused with `MemoryNotFound`. */
export function getWorkstreamBrief(name: string): Promise<WorkstreamBriefView> {
  return call(api.GET("/api/memory/workstreams/{name}", { params: { path: { name } } }));
}

/** The workstream list, fetched when the view opens and again when the window regains focus; never polled. */
export function useWorkstreams() {
  return useQuery({ queryKey: WORKSTREAMS_QUERY_KEY, queryFn: getWorkstreams, refetchOnWindowFocus: true });
}

/** One workstream's brief, fetched as the list is; `null` fetches nothing, and a missing one is not asked for again. */
export function useWorkstreamBrief(name: string | null) {
  return useQuery({
    queryKey: [...WORKSTREAMS_QUERY_KEY, name],
    queryFn: () => getWorkstreamBrief(name!),
    enabled: name !== null,
    refetchOnWindowFocus: true,
    retry: false,
  });
}

/** The query key of the activity months; one month's key adds the month. */
export const ACTIVITY_MONTHS_QUERY_KEY = ["memory", "activity"] as const;

/** The months of the activity log that have a file, newest first, and the current month. Counts nothing. */
export function getActivityMonths(): Promise<ActivityMonths> {
  return call(api.GET("/api/memory/activity"));
}

/**
 * One `yyyy-MM` month of the activity log by day. Counts nothing; a month with no file is refused with `MemoryNotFound`,
 * unless it is the current one.
 */
export function getActivityMonth(month: string): Promise<ActivityMonthView> {
  return call(api.GET("/api/memory/activity/{month}", { params: { path: { month } } }));
}

/** The activity months, fetched when the view opens and again when the window regains focus; never polled. */
export function useActivityMonths() {
  return useQuery({ queryKey: ACTIVITY_MONTHS_QUERY_KEY, queryFn: getActivityMonths, refetchOnWindowFocus: true });
}

/** One month, fetched as the months are; a missing one is not asked for again. */
export function useActivityMonth(month: string) {
  return useQuery({
    queryKey: [...ACTIVITY_MONTHS_QUERY_KEY, month],
    queryFn: () => getActivityMonth(month),
    refetchOnWindowFocus: true,
    retry: false,
  });
}
