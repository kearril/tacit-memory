import { describe, expect, it } from "vitest";
import type { ServiceLogDay, ServiceLogEntry } from "../../api/logs";
import { appendPage, dayTurned, isAtBottom, unseenOf } from "./follow";
import { ALL_LEVELS } from "../../lib/routes";

const entry = (level: ServiceLogEntry["level"], message: string): ServiceLogEntry => ({ time: null, level, message });

const page = (
  entries: readonly ServiceLogEntry[],
  next: number,
  date = "2026-11-14",
  today = "2026-11-14",
): ServiceLogDay => ({
  date,
  today,
  entries: [...entries],
  next,
});

describe("appendPage", () => {
  it("adds the new events after the ones held and takes the new count", () => {
    const held = { entries: [entry("information", "一")], next: 1 };
    expect(appendPage(held, page([entry("warning", "二")], 2))).toEqual({
      entries: [entry("information", "一"), entry("warning", "二")],
      next: 2,
    });
  });

  it("keeps what it holds when nothing is new", () => {
    const held = { entries: [entry("information", "一")], next: 1 };
    expect(appendPage(held, page([], 1))).toBe(held);
  });
});

describe("dayTurned", () => {
  it("is the new day once the service's today is past the day followed", () => {
    expect(dayTurned(page([], 3, "2026-11-14", "2026-11-15"))).toBe("2026-11-15");
  });

  it("is nothing while the day followed is today", () => {
    expect(dayTurned(page([], 3))).toBeUndefined();
  });
});

describe("isAtBottom", () => {
  it("counts a view within the tolerance of the end as at the bottom", () => {
    expect(isAtBottom({ scrollTop: 960, clientHeight: 800, scrollHeight: 1800 })).toBe(true);
    expect(isAtBottom({ scrollTop: 1000, clientHeight: 800, scrollHeight: 1800 })).toBe(true);
  });

  it("does not once the reader has scrolled up", () => {
    expect(isAtBottom({ scrollTop: 600, clientHeight: 800, scrollHeight: 1800 })).toBe(false);
  });
});

describe("unseenOf", () => {
  it("counts only the new events the filters let through", () => {
    const fresh = [entry("information", "recall"), entry("warning", "绑定失败"), entry("error", "备份失败")];
    expect(unseenOf(fresh, { levels: new Set(["warning", "error"]), query: "" })).toBe(2);
    expect(unseenOf(fresh, { levels: ALL_LEVELS, query: "失败" })).toBe(2);
    expect(unseenOf([], { levels: ALL_LEVELS, query: "" })).toBe(0);
  });
});
