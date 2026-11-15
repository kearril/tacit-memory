import { describe, expect, it } from "vitest";
import type { ActivityReport } from "../dashboard/heatmap";
import { MINI_WEEKS, weekLevel, workstreamWeeks } from "./workstreams";

/** A year-long window ending on Wednesday 2026-11-11, as the service gives it: from Monday 2025-11-10. */
function report(days: ActivityReport["days"]): ActivityReport {
  return {
    from: "2025-11-10",
    to: "2026-11-11",
    days,
    consolidationDays: [],
    skippedFiles: [],
    historyUnreadable: false,
  };
}

describe("workstreamWeeks", () => {
  it("counts one workstream's activities per week over the last weeks, the current week last", () => {
    const weeks = workstreamWeeks(
      report([
        { day: "2026-11-09", workstreams: [{ workstream: "my-app", count: 2 }], unlabeled: 1 },
        {
          day: "2026-11-11",
          workstreams: [
            { workstream: "other", count: 5 },
            { workstream: "my-app", count: 1 },
          ],
          unlabeled: 0,
        },
        { day: "2026-11-08", workstreams: [{ workstream: "my-app", count: 4 }], unlabeled: 0 },
      ]),
      "my-app",
    );

    expect(weeks).toHaveLength(MINI_WEEKS);
    expect(weeks.at(-1)).toEqual({ monday: "2026-11-09", count: 3 });
    expect(weeks.at(-2)).toEqual({ monday: "2026-11-02", count: 4 });
    expect(weeks[0]).toEqual({ monday: "2026-05-18", count: 0 });
  });

  it("leaves out the weeks before the window's first", () => {
    const weeks = workstreamWeeks(
      {
        ...report([{ day: "2026-11-02", workstreams: [{ workstream: "my-app", count: 1 }], unlabeled: 0 }]),
        from: "2026-11-02",
      },
      "my-app",
    );

    expect(weeks).toEqual([
      { monday: "2026-11-02", count: 1 },
      { monday: "2026-11-09", count: 0 },
    ]);
  });
});

describe("weekLevel", () => {
  it("shades a week by its count on the heatmap's four steps, scaled to a week", () => {
    expect([0, 1, 3, 4, 7, 8, 14, 15].map(weekLevel)).toEqual([0, 1, 1, 2, 2, 3, 3, 4]);
  });
});
