import { describe, expect, it } from "vitest";
import { clockOf, durationOf, remainingOf, sizeOf } from "./duration";

describe("durationOf", () => {
  it("splits seconds into whole days, hours and minutes", () => {
    expect(durationOf(3 * 86400 + 4 * 3600 + 12 * 60 + 59)).toEqual({ days: 3, hours: 4, minutes: 12 });
  });

  it("is all zero below a minute, and for a negative count", () => {
    expect(durationOf(59)).toEqual({ days: 0, hours: 0, minutes: 0 });
    expect(durationOf(-5)).toEqual({ days: 0, hours: 0, minutes: 0 });
  });
});

describe("remainingOf", () => {
  it("counts down by the whole seconds since the report was fetched, and stops at zero", () => {
    expect(remainingOf(120, 10_000, 10_999)).toBe(120);
    expect(remainingOf(120, 10_000, 13_500)).toBe(117);
    expect(remainingOf(2, 10_000, 15_000)).toBe(0);
  });
});

describe("clockOf", () => {
  it("shows minutes and two-digit seconds, and hours from an hour up", () => {
    expect(clockOf(5)).toBe("0:05");
    expect(clockOf(272)).toBe("4:32");
    expect(clockOf(3723)).toBe("1:02:03");
  });
});

describe("sizeOf", () => {
  it("takes the largest unit that keeps the number at one or more, with at most one decimal", () => {
    expect(sizeOf(512, "en")).toBe("512 byte");
    expect(sizeOf(1536, "en")).toBe("1.5 kB");
    expect(sizeOf(3.44 * 1024 * 1024, "en")).toBe("3.4 MB");
  });
});
