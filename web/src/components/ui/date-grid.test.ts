import { describe, expect, it } from "vitest";
import { addDays, addMonths, isDate, monthWeeks } from "./date-grid";

describe("isDate", () => {
  it("takes real calendar dates only", () => {
    expect(isDate("2028-02-29")).toBe(true);
    expect(isDate("2027-02-29")).toBe(false);
    expect(isDate("2026-13-01")).toBe(false);
    expect(isDate("2026-1-01")).toBe(false);
    expect(isDate("")).toBe(false);
  });
});

describe("addDays", () => {
  it("steps across months and years", () => {
    expect(addDays("2026-10-31", 1)).toBe("2026-11-01");
    expect(addDays("2027-01-01", -1)).toBe("2026-12-31");
    expect(addDays("2026-11-16", 7)).toBe("2026-11-23");
  });
});

describe("addMonths", () => {
  it("keeps the day within the shorter month", () => {
    expect(addMonths("2026-01-31", 1)).toBe("2026-02-28");
    expect(addMonths("2028-01-31", 1)).toBe("2028-02-29");
    expect(addMonths("2026-03-15", -1)).toBe("2026-02-15");
    expect(addMonths("2026-12-10", 1)).toBe("2027-01-10");
  });
});

describe("monthWeeks", () => {
  it("lays the month out in whole weeks from Monday, with the days of the months around it", () => {
    const weeks = monthWeeks("2026-11-16");

    expect(weeks).toHaveLength(6);
    expect(weeks[0]).toEqual([
      "2026-10-26",
      "2026-10-27",
      "2026-10-28",
      "2026-10-29",
      "2026-10-30",
      "2026-10-31",
      "2026-11-01",
    ]);
    expect(weeks[5]?.[0]).toBe("2026-11-30");
    expect(weeks[5]?.[6]).toBe("2026-12-06");
  });

  it("starts on the first when the month begins on a Monday", () => {
    expect(monthWeeks("2027-02-01")[0]?.[0]).toBe("2027-02-01");
    expect(monthWeeks("2027-02-01")).toHaveLength(4);
  });
});
