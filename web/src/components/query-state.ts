import { ApiError } from "../api/client";

/**
 * Where a query stands, as a page shows it. Data once read stays shown: a read again that fails in the background does
 * not take it away. A first read that fails is `missing` when the service answers that the thing asked for does not
 * exist (404), and `failed` for any other failure, an answer or none.
 */
export type QueryState<T> =
  | { readonly kind: "loading" }
  | { readonly kind: "ready"; readonly data: T }
  | { readonly kind: "missing"; readonly error: ApiError }
  | { readonly kind: "failed"; readonly error: Error };

/** The state of a query from what it holds: its data, and the error of its last read. */
export function queryStateOf<T>(query: { readonly data: T | undefined; readonly error: Error | null }): QueryState<T> {
  if (query.data !== undefined) {
    return { kind: "ready", data: query.data };
  }
  if (query.error === null) {
    return { kind: "loading" };
  }
  if (query.error instanceof ApiError && query.error.status === 404) {
    return { kind: "missing", error: query.error };
  }
  return { kind: "failed", error: query.error };
}
