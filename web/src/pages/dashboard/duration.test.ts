import { describe, expect, it } from "vitest";
import { durationOf } from "./duration";

describe("durationOf", () => {
  it("splits seconds into whole days, hours and minutes", () => {
    expect(durationOf(3 * 86400 + 4 * 3600 + 12 * 60 + 59)).toEqual({ days: 3, hours: 4, minutes: 12 });
  });

  it("is all zero below a minute, and for a negative count", () => {
    expect(durationOf(59)).toEqual({ days: 0, hours: 0, minutes: 0 });
    expect(durationOf(-5)).toEqual({ days: 0, hours: 0, minutes: 0 });
  });
});
