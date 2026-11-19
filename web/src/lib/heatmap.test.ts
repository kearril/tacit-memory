import { describe, expect, it } from "vitest";
import {
  type ActivityReport,
  calendarColumns,
  dayLevel,
  fillWindow,
  type HeatDay,
  monthLabels,
  statsOf,
} from "./heatmap";

/** A short window of whole weeks ending on Wednesday 2026-11-11: from Monday 2026-05-18, 26 weeks in all. */
function report(overrides: Partial<ActivityReport> = {}): ActivityReport {
  return {
    from: "2026-05-18",
    to: "2026-11-11",
    days: [],
    consolidationDays: [],
    skippedFiles: [],
    historyUnreadable: false,
    ...overrides,
  };
}

/** A filled day with only unlabeled activities. */
function day(date: string, total: number): HeatDay {
  return { date, total, workstreams: [], unlabeled: total, consolidated: false };
}

describe("fillWindow", () => {
  it("fills every day from the first day to today, with the listed days' counts and the consolidation days", () => {
    const days = fillWindow(
      report({
        days: [
          { day: "2026-05-18", workstreams: [{ workstream: "tacit-memory", count: 3 }], unlabeled: 1 },
          { day: "2026-11-11", workstreams: [], unlabeled: 2 },
        ],
        consolidationDays: ["2026-06-01"],
      }),
    );

    expect(days).toHaveLength(25 * 7 + 3);
    expect(days[0]).toEqual({
      date: "2026-05-18",
      total: 4,
      workstreams: [{ workstream: "tacit-memory", count: 3 }],
      unlabeled: 1,
      consolidated: false,
    });
    expect(days[1]).toEqual({ date: "2026-05-19", total: 0, workstreams: [], unlabeled: 0, consolidated: false });
    expect(days.find((item) => item.date === "2026-06-01")?.consolidated).toBe(true);
    expect(days.at(-1)).toMatchObject({ date: "2026-11-11", total: 2 });
  });

  it("crosses the month and daylight-saving boundaries one calendar day at a time", () => {
    const days = fillWindow(report({ from: "2026-10-19", to: "2026-11-02" }));

    expect(days.map((item) => item.date)).toContain("2026-10-25");
    expect(days.map((item) => item.date)).toContain("2026-10-31");
    expect(days.map((item) => item.date)).toContain("2026-11-01");
    expect(days).toHaveLength(15);
  });
});

describe("calendarColumns", () => {
  it("makes one column of Monday to Sunday per week of the window, with empty places after today", () => {
    const columns = calendarColumns(fillWindow(report()));

    expect(columns).toHaveLength(26);
    expect(columns.every((week) => week.length === 7)).toBe(true);
    expect(columns[0][0]?.date).toBe("2026-05-18");
    expect(columns[0][6]?.date).toBe("2026-05-24");
    expect(columns[25].map((item) => item?.date ?? null)).toEqual([
      "2026-11-09",
      "2026-11-10",
      "2026-11-11",
      null,
      null,
      null,
      null,
    ]);
  });
});

describe("monthLabels", () => {
  it("labels the first column of each new month, and the first column only when its month has just begun", () => {
    const columns = calendarColumns(fillWindow(report()));

    const labels = monthLabels(columns);

    // 2026-05-18 is late in May, so the first column carries no label; June starts in the column of Monday 2026-06-01.
    expect(labels[0]).toEqual({ column: 2, date: "2026-06-01" });
    expect(labels.map((label) => label.date.slice(0, 7))).toEqual([
      "2026-06",
      "2026-07",
      "2026-08",
      "2026-09",
      "2026-10",
      "2026-11",
    ]);
  });

  it("labels the first column when its Monday is in the first week of its month", () => {
    const labels = monthLabels(calendarColumns(fillWindow(report({ from: "2026-06-01", to: "2026-06-14" }))));

    expect(labels).toEqual([{ column: 0, date: "2026-06-01" }]);
  });
});

describe("dayLevel", () => {
  it("shades a day by its count: 0, up to 2, up to 4, up to 7, 8 or more", () => {
    expect([0, 1, 2, 3, 4, 5, 7, 8, 30].map(dayLevel)).toEqual([0, 1, 1, 2, 2, 3, 3, 4, 4]);
  });
});

describe("statsOf", () => {
  it("counts the total, the active days and the longest run of active days", () => {
    const days = [day("a", 2), day("b", 1), day("c", 0), day("d", 3), day("e", 1), day("f", 4), day("g", 0)];

    expect(statsOf(days)).toMatchObject({ total: 11, activeDays: 5, longestStreak: 3 });
  });

  it("counts a run that touches either end of the window only within it", () => {
    expect(statsOf([day("a", 1), day("b", 1), day("c", 0)]).longestStreak).toBe(2);
    expect(statsOf([day("a", 0), day("b", 1), day("c", 1), day("d", 1)]).longestStreak).toBe(3);
  });

  it("is all zero with no top workstream when nothing was recorded", () => {
    expect(statsOf([day("a", 0), day("b", 0)])).toEqual({
      total: 0,
      activeDays: 0,
      longestStreak: 0,
      topWorkstream: null,
    });
  });

  it("names the workstream with the most activities over the window, a tie going to the first name", () => {
    const days: HeatDay[] = [
      {
        date: "a",
        total: 3,
        workstreams: [
          { workstream: "my-app", count: 2 },
          { workstream: "tacit-memory", count: 1 },
        ],
        unlabeled: 0,
        consolidated: false,
      },
      {
        date: "b",
        total: 9,
        workstreams: [{ workstream: "tacit-memory", count: 1 }],
        unlabeled: 8,
        consolidated: false,
      },
    ];

    // Unlabeled activities never make a top workstream; my-app and tacit-memory tie at 2.
    expect(statsOf(days).topWorkstream).toEqual({ workstream: "my-app", count: 2 });
  });
});
