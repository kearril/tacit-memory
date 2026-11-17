import { ApiError, type TacitError } from "../api/client";
import type { ConfigProblem } from "../api/config";

/**
 * What kind of failure a command met, for the page to show it by: refused with every problem, each shown where it is;
 * refused because the file was changed since it was read, which reading it again resolves; any other answer of the
 * service; or no answer at all. The text to show comes from the error (see `errorTextOf`).
 */
export type CommandFailure =
  | { readonly kind: "rejected"; readonly problems: readonly ConfigProblem[]; readonly error: ApiError }
  | { readonly kind: "changed"; readonly error: ApiError }
  | { readonly kind: "failed"; readonly error: ApiError }
  | { readonly kind: "unreachable"; readonly error: Error };

/** A failure as a page shows it once it happened: what failed, and the failure. */
export interface TitledFailure {
  readonly title: string;
  readonly failure: CommandFailure;
}

/** The names of a refusal that says the file was changed after the page read it. */
const CHANGED: readonly TacitError[] = ["ConfigChanged", "PromptChanged"];

/** The kind of failure `error` is. Listed problems decide first, as they are what the page shows. */
export function classifyFailure(error: Error): CommandFailure {
  if (!(error instanceof ApiError)) {
    return { kind: "unreachable", error };
  }
  if (error.problems.length > 0) {
    return { kind: "rejected", problems: error.problems, error };
  }
  if (error.error !== null && CHANGED.includes(error.error)) {
    return { kind: "changed", error };
  }
  return { kind: "failed", error };
}
