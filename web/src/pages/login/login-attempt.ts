/**
 * The state of the login form, as a pure reducer so its rules can be checked without rendering. A 429 locks the submit
 * until the password field is edited; a wrong password leaves it open.
 */
export type LoginAttempt =
  | { readonly phase: "idle" }
  | { readonly phase: "pending" }
  | { readonly phase: "failed"; readonly message: string }
  | { readonly phase: "limited"; readonly message: string };

export type LoginAttemptEvent =
  | { readonly type: "submitted" }
  | { readonly type: "succeeded" }
  | { readonly type: "edited" }
  | { readonly type: "failed"; readonly error: { readonly status: number; readonly message: string } };

export const initialLoginAttempt: LoginAttempt = { phase: "idle" };

export function loginAttemptReducer(state: LoginAttempt, event: LoginAttemptEvent): LoginAttempt {
  switch (event.type) {
    case "submitted":
      // A limited form stays locked: the submit is disabled until the password is edited.
      return state.phase === "limited" ? state : { phase: "pending" };
    case "succeeded":
      return { phase: "idle" };
    case "edited":
      return { phase: "idle" };
    case "failed":
      return event.error.status === 429
        ? { phase: "limited", message: event.error.message }
        : { phase: "failed", message: event.error.message };
  }
}

/** Whether the submit button is enabled in the given state. */
export function canSubmitLogin(state: LoginAttempt): boolean {
  return state.phase === "idle" || state.phase === "failed";
}
