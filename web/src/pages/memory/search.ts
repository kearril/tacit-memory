import type { RecallTraceRequest } from "../../api/memory";
import type { SearchFields } from "../../lib/routes";

/** The trace request for the conditions: the query as typed, an empty condition left out, a value sent as a number. */
export function requestOf(fields: SearchFields): RecallTraceRequest {
  const text = (value: string) => (value.trim() === "" ? null : value.trim());
  const number = (value: string) => (value.trim() === "" ? null : Number(value));
  return {
    query: fields.query,
    workstream: text(fields.workstream),
    from: text(fields.from),
    to: text(fields.to),
    kind: text(fields.kind),
    minRerankScore: number(fields.minRerankScore),
    topK: number(fields.topK),
    candidates: number(fields.candidates),
  };
}
