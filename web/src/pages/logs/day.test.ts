import { describe, expect, it } from "vitest";
import { clockOf, dateOptions } from "./day";

describe("dateOptions", () => {
  it("puts today first even before its file exists", () => {
    expect(dateOptions({ today: "2026-11-14", dates: ["2026-11-13", "2026-11-10"] })).toEqual([
      "2026-11-14",
      "2026-11-13",
      "2026-11-10",
    ]);
  });

  it("lists today once when it has a file", () => {
    expect(dateOptions({ today: "2026-11-14", dates: ["2026-11-14", "2026-11-13"] })).toEqual([
      "2026-11-14",
      "2026-11-13",
    ]);
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
