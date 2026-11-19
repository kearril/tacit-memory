import { describe, expect, it } from "vitest";
import { initialLoginAttempt, loginAttemptReducer, type LoginAttempt } from "./login-attempt";

const wrongPassword = { status: 401, error: "PasswordWrong", message: "访问密码错误。" };
const limited = { status: 429, error: "LoginLimited", message: "错误次数过多，请 47 秒后再试。" };

describe("loginAttemptReducer", () => {
  it("starts idle with the submit enabled", () => {
    expect(initialLoginAttempt).toEqual({ phase: "idle" });
  });

  it("is pending while a submit is in flight, which disables the submit button", () => {
    const next = loginAttemptReducer(initialLoginAttempt, { type: "submitted" });
    expect(next).toEqual({ phase: "pending" });
  });

  it("shows the service's explanation for a wrong password and keeps the submit enabled", () => {
    const next = loginAttemptReducer({ phase: "pending" }, { type: "failed", error: wrongPassword });
    expect(next).toEqual({ phase: "failed", message: "访问密码错误。" });
  });

  it("is limited on a 429 and shows the service's explanation verbatim", () => {
    const next = loginAttemptReducer({ phase: "pending" }, { type: "failed", error: limited });
    expect(next).toEqual({ phase: "limited", message: "错误次数过多，请 47 秒后再试。" });
  });

  it("stays limited on further submits until the password field is edited", () => {
    const limitedState: LoginAttempt = { phase: "limited", message: "x" };
    expect(loginAttemptReducer(limitedState, { type: "submitted" })).toEqual(limitedState);
    expect(loginAttemptReducer(limitedState, { type: "edited" })).toEqual({ phase: "idle" });
  });

  it("clears a failure message when the password field is edited", () => {
    const failed: LoginAttempt = { phase: "failed", message: "x" };
    expect(loginAttemptReducer(failed, { type: "edited" })).toEqual({ phase: "idle" });
  });

  it("resets to idle after a successful login", () => {
    const next = loginAttemptReducer({ phase: "pending" }, { type: "succeeded" });
    expect(next).toEqual({ phase: "idle" });
  });
});
