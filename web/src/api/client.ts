import createClient from "openapi-fetch";
import type { components, paths } from "./schema";

type ErrorBody = components["schemas"]["ApiError"];
type ConfigProblem = components["schemas"]["ConfigProblem"];

/** The name of an error the service gives, one of its TacitError names. */
export type TacitError = components["schemas"]["TacitError"];

/** An error body as read: the service's, or one without a name or an explanation when the answer was not its JSON. */
type ReadErrorBody = ErrorBody | { readonly error: null; readonly message: "" };

/**
 * A failed call to the management API. `error` is the service's TacitError name, `null` when the answer is not the
 * service's JSON; `message` is the explanation in the memory language, shown to the user as it is. It is empty when the answer carried no explanation, and then the text to show
 * comes from the dictionary (see `errorTextOf`). `problems` lists each place a refused config or document went wrong, and is
 * empty for every other refusal.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly error: TacitError | null;
  readonly problems: readonly ConfigProblem[];

  constructor(status: number, body: ReadErrorBody) {
    super(body.message);
    this.name = "ApiError";
    this.status = status;
    this.error = body.error;
    this.problems = ("problems" in body ? body.problems : null) ?? [];
  }
}

type UnauthorizedListener = (error: ApiError) => void;

/** The error name the service gives when a call needs a session that is missing or no longer valid. */
const SESSION_MISSING: TacitError = "ApiUnauthorized";

const unauthorizedListeners = new Set<UnauthorizedListener>();

/**
 * Subscribes to the moment a call is answered that the session is missing or no longer valid. Other 401 answers, such as
 * a wrong current password, are answers to the request itself and are not signalled. Returns the function that
 * unsubscribes. What happens next (the login page) is decided by whoever subscribes.
 */
export function onUnauthorized(listener: UnauthorizedListener): () => void {
  unauthorizedListeners.add(listener);
  return () => {
    unauthorizedListeners.delete(listener);
  };
}

/**
 * The management API, typed by its OpenAPI paths: the path, the parameters, the body and the answer of each call are
 * checked against the document. Every call goes to the page's own origin and carries the credentials, so the session
 * cookie goes with it. Pages call it through {@link call}.
 */
export const api = createClient<paths>({
  baseUrl: location.origin,
  credentials: "include",
  // Looked up on each call rather than kept from the start, so a fetch put in place later is the one used.
  fetch: (request) => fetch(request),
});

api.use({
  /** The service refuses a post or put that is not JSON, even one without a body. */
  onRequest({ request }) {
    if ((request.method === "POST" || request.method === "PUT") && !request.headers.has("Content-Type")) {
      request.headers.set("Content-Type", "application/json");
    }
    return request;
  },
  /** A missing session signals the subscribers of {@link onUnauthorized} before the call's own failure is thrown. */
  async onResponse({ response }) {
    if (response.status === 401) {
      const error = new ApiError(response.status, errorBodyOf(await response.clone().text()));
      if (error.error === SESSION_MISSING) {
        for (const listener of unauthorizedListeners) {
          listener(error);
        }
      }
    }
    return response;
  },
});

/**
 * The answer of a call to {@link api}: its data, or an {@link ApiError} thrown when it failed. An answer without a body
 * resolves to `undefined`.
 */
export async function call<D>(pending: Promise<{ data?: D; error?: unknown; response: Response }>): Promise<D> {
  const { data, error, response } = await pending;
  if (!response.ok) {
    throw new ApiError(response.status, errorBodyOf(error));
  }
  return data as D;
}

/** The service's error body from what the answer carried, or one without a name or explanation when it is not the service's JSON. */
function errorBodyOf(body: unknown): ReadErrorBody {
  let value = body;
  if (typeof value === "string") {
    try {
      value = JSON.parse(value);
    } catch {
      // Not JSON: fall through to the body without an explanation.
    }
  }
  return isErrorBody(value) ? value : { error: null, message: "" };
}

function isErrorBody(value: unknown): value is ErrorBody {
  return (
    typeof value === "object" &&
    value !== null &&
    typeof (value as Record<string, unknown>).error === "string" &&
    typeof (value as Record<string, unknown>).message === "string"
  );
}
