import type { RecallTraceRequest } from "../../api/memory";

/** The search view's conditions as typed; an empty string means left out. They live in the address. */
export interface SearchFields {
  readonly query: string;
  readonly workstream: string;
  readonly from: string;
  readonly to: string;
  readonly kind: "" | "pages" | "activities";
  readonly minRerankScore: string;
  readonly topK: string;
  readonly candidates: string;
}

export const EMPTY_SEARCH: SearchFields = {
  query: "",
  workstream: "",
  from: "",
  to: "",
  kind: "",
  minRerankScore: "",
  topK: "",
  candidates: "",
};

/** Each field's name in the address. */
const PARAMS: Readonly<Record<keyof SearchFields, string>> = {
  query: "q",
  workstream: "workstream",
  from: "from",
  to: "to",
  kind: "kind",
  minRerankScore: "min",
  topK: "topK",
  candidates: "candidates",
};

const DATE = /^\d{4}-(0[1-9]|1[0-2])-(0[1-9]|[12]\d|3[01])$/;
const COUNT = /^[1-9]\d*$/;
const SCORE = /^\d+(\.\d+)?$/;

/** The conditions an address holds. A value the view does not know (a kind, a date, a number) is ignored. */
export function searchFieldsOf(params: URLSearchParams): SearchFields {
  const read = (key: keyof SearchFields, valid: (value: string) => boolean = () => true) => {
    const value = params.get(PARAMS[key])?.trim() ?? "";
    return valid(value) ? value : "";
  };
  const kind = read("kind");
  return {
    // The query is kept as typed: recall embeds it as given, so a space changes its vector.
    query: params.get(PARAMS.query) ?? "",
    workstream: read("workstream"),
    from: read("from", (value) => DATE.test(value)),
    to: read("to", (value) => DATE.test(value)),
    kind: kind === "pages" || kind === "activities" ? kind : "",
    minRerankScore: read("minRerankScore", (value) => SCORE.test(value)),
    topK: read("topK", (value) => COUNT.test(value)),
    candidates: read("candidates", (value) => COUNT.test(value)),
  };
}

/** The address of the conditions: only those that are set, in a fixed order. */
export function searchParamsOf(fields: SearchFields): URLSearchParams {
  const params = new URLSearchParams();
  for (const key of Object.keys(PARAMS) as (keyof SearchFields)[]) {
    const value = fields[key];
    if (value.trim() !== "") {
      params.set(PARAMS[key], key === "query" ? value : value.trim());
    }
  }
  return params;
}

/** Whether any of the three retrieval values is replaced. */
export function hasOverrides(fields: SearchFields): boolean {
  return fields.minRerankScore !== "" || fields.topK !== "" || fields.candidates !== "";
}

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
