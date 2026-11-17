import { describe, expect, it } from "vitest";
import { ApiError } from "../api/client";
import { en } from "./en";
import { errorTextOf } from "./messages";
import { zh } from "./zh";

describe("errorTextOf", () => {
  it("shows the service's explanation when it gave one", () => {
    const error = new ApiError(400, { error: "PasswordTooShort", message: "访问密码至少需要 8 个字符。" });

    expect(errorTextOf(error, zh)).toBe("访问密码至少需要 8 个字符。");
  });

  it("shows the dictionary's text for an answer without an explanation, in each memory language", () => {
    const error = new ApiError(502, { error: null, message: "" });

    expect(errorTextOf(error, zh)).toBe(zh.unexpectedAnswer);
    expect(errorTextOf(error, en)).toBe(en.unexpectedAnswer);
  });

  it("shows the unreachable text when no answer came at all", () => {
    expect(errorTextOf(new TypeError("Failed to fetch"), en)).toBe(en.unreachable);
    expect(errorTextOf(undefined, zh)).toBe(zh.unreachable);
  });
});
