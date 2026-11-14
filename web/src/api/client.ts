import type { components } from "./schema";

type ErrorBody = components["schemas"]["ApiError"];
type ConfigProblem = components["schemas"]["ConfigProblem"];

/**
 * A failed call to the management API. `error` is the service's TacitError name; `message` is the explanation in the memory
 * language, shown to the user as it is. It is empty when the answer carried no explanation, and then the text to show
 * comes from the dictionary (see `errorTextOf`). `problems` lists each place a refused config or document went wrong, and is
 * empty for every other refusal.
 */
export class ApiError extends Error {
  readonly status: number;
  readonly error: string;
  readonly problems: readonly ConfigProblem[];

  constructor(status: number, body: ErrorBody) {
    super(body.message);
    this.name = "ApiError";
    this.status = status;
    this.error = body.error;
    this.problems = body.problems ?? [];
  }
}

type UnauthorizedListener = (error: ApiError) => void;

/** The error name the service gives when a call needs a session that is missing or no longer valid. */
const SESSION_MISSING = "ApiUnauthorized";

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
 * Calls the management API at `path` on the same origin. Every call carries the credentials, so the session cookie goes
 * with it. A failed answer throws an {@link ApiError}; when the session is missing, the subscribers of
 * {@link onUnauthorized} are signalled first. Pages call the API only through this function.
 */
export async function apiFetch<T>(path: string, init: RequestInit = {}): Promise<T> {
  const headers = new Headers(init.headers);
  if (init.body !== undefined && init.body !== null && !headers.has("Content-Type")) {
    headers.set("Content-Type", "application/json");
  }
  const response = await fetch(path, { ...init, headers, credentials: "include" });

  if (!response.ok) {
    const error = new ApiError(response.status, await readErrorBody(response));
    if (response.status === 401 && error.error === SESSION_MISSING) {
      for (const listener of unauthorizedListeners) {
        listener(error);
      }
    }
    throw error;
  }

  const text = await response.text();
  return (text.length === 0 ? undefined : JSON.parse(text)) as T;
}

/** The service's error body, or an empty explanation when the answer is not the service's JSON. */
async function readErrorBody(response: Response): Promise<ErrorBody> {
  try {
    const parsed: unknown = JSON.parse(await response.text());
    if (isErrorBody(parsed)) {
      return parsed;
    }
  } catch {
    // Not JSON: fall through to the body without an explanation.
  }
  return { error: "unexpected", message: "" };
}

function isErrorBody(value: unknown): value is ErrorBody {
  return (
    typeof value === "object" &&
    value !== null &&
    typeof (value as Record<string, unknown>).error === "string" &&
    typeof (value as Record<string, unknown>).message === "string"
  );
}
