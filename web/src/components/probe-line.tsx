import type { ProbeResult } from "../api/probe";
import { useDictionary } from "../app/session-context";

/** What the last probe of one model found, on a small line under the model: success, a skip, or how it failed. */
export function ProbeLine({ result }: { readonly result: ProbeResult }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.probe;
  const milliseconds = result.elapsedMilliseconds ?? 0;
  if (result.outcome === "succeeded") {
    return <span className="text-xs text-success">{text.succeeded(milliseconds)}</span>;
  }
  if (result.outcome === "unconfigured") {
    return <span className="text-xs text-ink-muted">{text.skipped}</span>;
  }
  const reason =
    result.failure === "httpStatus" && result.statusCode !== null
      ? text.httpStatus(result.statusCode)
      : result.failure === "wrongFormat"
        ? text.wrongFormat
        : result.failure === "timeout"
          ? text.timeout
          : text.network;
  return (
    <span title={result.responseBody ?? undefined} className="truncate text-xs text-danger">
      {text.failed(reason, milliseconds)}
    </span>
  );
}
