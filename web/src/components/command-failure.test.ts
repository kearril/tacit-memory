import { describe, expect, it } from "vitest";
import { ApiError } from "../api/client";
import { classifyFailure } from "./command-failure";

describe("classifyFailure", () => {
  it("is rejected with every problem when the service lists them", () => {
    const problems = [{ path: "backup.keep", message: "超出范围" }];
    const error = new ApiError(400, { error: "ConfigRejected", message: "超出范围", problems });

    expect(classifyFailure(error)).toEqual({ kind: "rejected", problems, error });
  });

  it("is changed when the file was changed since it was read, so reading it again helps", () => {
    const config = new ApiError(409, { error: "ConfigChanged", message: "config.yaml 被修改过。" });
    const prompt = new ApiError(409, { error: "PromptChanged", message: "提示词文档被修改过。" });

    expect(classifyFailure(config)).toEqual({ kind: "changed", error: config });
    expect(classifyFailure(prompt)).toEqual({ kind: "changed", error: prompt });
  });

  it("is failed for any other answer of the service", () => {
    const error = new ApiError(409, { error: "BackupExists", message: "备份已存在。" });

    expect(classifyFailure(error)).toEqual({ kind: "failed", error });
  });

  it("is failed for an answer that is not the service's", () => {
    const error = new ApiError(502, { error: null, message: "" });

    expect(classifyFailure(error)).toEqual({ kind: "failed", error });
  });

  it("is unreachable when no answer came at all", () => {
    const error = new TypeError("Failed to fetch");

    expect(classifyFailure(error)).toEqual({ kind: "unreachable", error });
  });

  it("is rejected by its problems even when its name is one of a changed file", () => {
    const problems = [{ path: "prompts/rules.md", message: "缺少节" }];
    const error = new ApiError(409, { error: "PromptChanged", message: "缺少节", problems });

    expect(classifyFailure(error)).toEqual({ kind: "rejected", problems, error });
  });
});
