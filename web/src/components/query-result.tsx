import type { UseQueryResult } from "@tanstack/react-query";
import { RefreshCw } from "lucide-react";
import type { ReactNode } from "react";
import type { ApiError } from "../api/client";
import { useDictionary } from "../app/session-context";
import { errorTextOf } from "../i18n/messages";
import { queryStateOf } from "./query-state";
import { Alert, AlertDescription, AlertTitle } from "./ui/alert";
import { BusyIcon } from "./ui/busy-icon";
import { Button } from "./ui/button";

/** Reads a query again; `busy` while that read is on its way. */
export interface Retry {
  readonly busy: boolean;
  readonly run: () => void;
}

/**
 * A query as a page shows it: `skeleton` while it loads, its data through `children` once read, and a failure with a
 * button to read again. A thing asked for that does not exist is shown by `missing`: a title over the service's
 * explanation, or what the function renders; without it, it is shown as any failure. `failure` replaces how a failure is
 * shown. Data once read stays shown when a read again fails in the background.
 */
export function QueryResult<T>({
  query,
  title,
  skeleton = <div aria-busy="true" />,
  missing,
  failure,
  children,
}: {
  readonly query: UseQueryResult<T>;
  readonly title: string;
  readonly skeleton?: ReactNode;
  readonly missing?: string | ((error: ApiError) => ReactNode);
  readonly failure?: (error: Error, retry: Retry) => ReactNode;
  readonly children: (data: T) => ReactNode;
}) {
  const dictionary = useDictionary();
  const state = queryStateOf(query);
  const retry: Retry = { busy: query.isFetching, run: () => void query.refetch() };

  switch (state.kind) {
    case "loading":
      return skeleton;
    case "ready":
      return children(state.data);
    case "missing":
      if (typeof missing === "function") {
        return missing(state.error);
      }
      if (missing !== undefined) {
        return (
          <Alert tone="warning">
            <AlertTitle>{missing}</AlertTitle>
            <AlertDescription>{errorTextOf(state.error, dictionary)}</AlertDescription>
          </Alert>
        );
      }
      break;
    case "failed":
      break;
  }
  return failure === undefined ? (
    <QueryFailed title={title} error={state.error} retry={retry} />
  ) : (
    failure(state.error, retry)
  );
}

/**
 * A read that failed: `title`, the service's explanation (or `children` in its place), and a button to read again.
 */
export function QueryFailed({
  title,
  error,
  retry,
  children,
}: {
  readonly title: string;
  readonly error: Error;
  readonly retry: Retry;
  readonly children?: ReactNode;
}) {
  const dictionary = useDictionary();
  return (
    <Alert tone="danger">
      <AlertTitle>{title}</AlertTitle>
      <AlertDescription className="grid gap-sm">
        {children ?? <p>{errorTextOf(error, dictionary)}</p>}
        <div>
          <Button type="button" variant="outline" disabled={retry.busy} onClick={retry.run}>
            <BusyIcon busy={retry.busy} icon={RefreshCw} />
            {dictionary.readAgain}
          </Button>
        </div>
      </AlertDescription>
    </Alert>
  );
}
