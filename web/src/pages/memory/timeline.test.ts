import { describe, expect, it } from "vitest";
import type { ActivityMonthView } from "../../api/memory";
import { activityParams, activityQueryOf, monthDays, shiftMonth, timelineOf } from "./timeline";

function item(code: string, workstream: string | null = null) {
  return { code, workstream, text: `活动 ${code}`, continuation: [] };
}

function month(overrides: Partial<ActivityMonthView> = {}): ActivityMonthView {
  return {
    month: "2026-10",
    file: { path: "recent/2026-10.md", problems: [] },
    days: [],
    summaries: [],
    ...overrides,
  };
}

function week(label: string, first: string, last: string) {
  return { kind: "week", label, first, last, text: `${label} 的汇总` };
}

describe("timelineOf", () => {
  it("puts the month summary first, then the days newest first, each week summary before the last day of its week", () => {
    const timeline = timelineOf(
      month({
        days: [
          { date: "2026-10-05", activities: [item("a")] },
          { date: "2026-10-07", activities: [item("b")] },
          { date: "2026-10-13", activities: [item("c")] },
        ],
        summaries: [
          week("2026-W41", "2026-10-05", "2026-10-11"),
          { kind: "month", label: "2026-10", first: "2026-10-01", last: "2026-10-31", text: "一个月" },
        ],
      }),
      null,
    );

    expect(timeline.map((entry) => (entry.kind === "day" ? entry.date : entry.summary.label))).toEqual([
      "2026-10",
      "2026-10-13",
      "2026-W41",
      "2026-10-07",
      "2026-10-05",
    ]);
  });

  it("puts a week summary whose days are outside the month before every day up to its last", () => {
    const timeline = timelineOf(
      month({
        days: [
          { date: "2026-10-01", activities: [item("a")] },
          { date: "2026-10-30", activities: [item("b")] },
        ],
        summaries: [week("2026-W40", "2026-09-28", "2026-10-04"), week("2026-W44", "2026-10-26", "2026-11-01")],
      }),
      null,
    );

    expect(timeline.map((entry) => (entry.kind === "day" ? entry.date : entry.summary.label))).toEqual([
      "2026-W44",
      "2026-10-30",
      "2026-W40",
      "2026-10-01",
    ]);
  });

  it("keeps only one workstream's activities when filtered, dropping the days left empty", () => {
    const timeline = timelineOf(
      month({
        days: [
          { date: "2026-10-05", activities: [item("a", "my-app"), item("b")] },
          { date: "2026-10-06", activities: [item("c", "other")] },
        ],
      }),
      "my-app",
    );

    expect(timeline).toEqual([{ kind: "day", date: "2026-10-05", activities: [item("a", "my-app")] }]);
  });
});

describe("monthDays", () => {
  it("lists every day of the month from the first, with its count or none", () => {
    const days = monthDays("2026-10", new Map([["2026-10-05", 3]]));

    expect(days).toHaveLength(31);
    expect(days[0]).toEqual({ date: "2026-10-01", count: 0 });
    expect(days[4]).toEqual({ date: "2026-10-05", count: 3 });
    expect(days[30]).toEqual({ date: "2026-10-31", count: 0 });
  });

  it("knows February's length in a leap year", () => {
    expect(monthDays("2028-02", new Map())).toHaveLength(29);
    expect(monthDays("2027-02", new Map())).toHaveLength(28);
  });
});

describe("shiftMonth", () => {
  it("moves across the year boundary", () => {
    expect(shiftMonth("2026-12", 1)).toBe("2027-01");
    expect(shiftMonth("2026-01", -1)).toBe("2025-12");
  });
});

describe("activityQueryOf", () => {
  it("reads the month, the day and the workstream from the address, the month falling back to the current one", () => {
    expect(activityQueryOf(new URLSearchParams("month=2026-10&day=2026-10-05&workstream=my-app"), "2026-11")).toEqual({
      month: "2026-10",
      day: "2026-10-05",
      workstream: "my-app",
    });
    expect(activityQueryOf(new URLSearchParams("month=latest&day=x"), "2026-11")).toEqual({
      month: "2026-11",
      day: null,
      workstream: null,
    });
  });

  it("takes the month from a day given alone", () => {
    expect(activityQueryOf(new URLSearchParams("day=2026-09-30"), "2026-11").month).toBe("2026-09");
  });

  it("writes only what is set", () => {
    expect(activityParams({ month: "2026-10", day: null, workstream: "my-app" }).toString()).toBe(
      "month=2026-10&workstream=my-app",
    );
  });
});
