import { describe, expect, it } from "vitest";
import { ApiError } from "../api/client";
import { queryStateOf } from "./query-state";

const refused = new ApiError(409, { error: "ConfigInvalid", message: "config.yaml 有误。" });
const notFound = new ApiError(404, { error: "MemoryNotFound", message: "找不到这个细节页。" });

describe("queryStateOf", () => {
  it("is loading while there is neither data nor an error", () => {
    expect(queryStateOf({ data: undefined, error: null })).toEqual({ kind: "loading" });
  });

  it("is ready with the data once it has come", () => {
    expect(queryStateOf({ data: { pages: [] }, error: null })).toEqual({ kind: "ready", data: { pages: [] } });
  });

  it("stays ready with the data it holds when a read again fails", () => {
    expect(queryStateOf({ data: "held", error: refused })).toEqual({ kind: "ready", data: "held" });
  });

  it("is failed with the error when the first read fails", () => {
    expect(queryStateOf({ data: undefined, error: refused })).toEqual({ kind: "failed", error: refused });
  });

  it("is failed when no answer came at all", () => {
    const unreachable = new TypeError("Failed to fetch");

    expect(queryStateOf({ data: undefined, error: unreachable })).toEqual({ kind: "failed", error: unreachable });
  });

  it("is missing when the service answers that the thing asked for does not exist", () => {
    expect(queryStateOf({ data: undefined, error: notFound })).toEqual({ kind: "missing", error: notFound });
  });
});
