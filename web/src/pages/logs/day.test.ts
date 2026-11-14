import { describe, expect, it } from "vitest";
import { clockOf, dateOptions, logDateOf, logPathOf } from "./day";

describe("logDateOf", () => {
  it.each(["2026-11-14", "2024-02-29"])("accepts the calendar date %s", (value) => {
    expect(logDateOf(value)).toBe(value);
  });

  it.each([undefined, "", "20261114", "2026-13-01", "2026-02-30", "2025-02-29", "today", "2026-11-14x"])("refuses %s", (value) => {
    expect(logDateOf(value)).toBeUndefined();
  });
});

describe("logPathOf", () => {
  it("leaves today out of the address", () => {
    expect(logPathOf("2026-11-14", "2026-11-14")).toBe("/logs");
  });

  it("names any other day", () => {
    expect(logPathOf("2026-11-13", "2026-11-14")).toBe("/logs/2026-11-13");
  });
});

describe("dateOptions", () => {
  it("puts today first even before its file exists", () => {
    expect(dateOptions({ today: "2026-11-14", dates: ["2026-11-13", "2026-11-10"] })).toEqual(["2026-11-14", "2026-11-13", "2026-11-10"]);
  });

  it("lists today once when it has a file", () => {
    expect(dateOptions({ today: "2026-11-14", dates: ["2026-11-14", "2026-11-13"] })).toEqual(["2026-11-14", "2026-11-13"]);
  });
});

describe("clockOf", () => {
  it("shows the time as written, whatever the browser's time zone", () => {
    expect(clockOf("2026-11-01T09:16:00.031+08:00")).toBe("09:16:00.031");
  });

  it("pads the milliseconds the JSON leaves out", () => {
    expect(clockOf("2026-11-01T09:16:00+08:00")).toBe("09:16:00.000");
    expect(clockOf("2026-11-01T09:16:00.5-05:00")).toBe("09:16:00.500");
  });

  it("has no time for an event without one", () => {
    expect(clockOf(null)).toBeUndefined();
  });
});
