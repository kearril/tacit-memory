import { describe, expect, it } from "vitest";
import type { BriefPage, BriefPageGroup } from "../../api/memory";
import type { ActivityReport } from "../dashboard/heatmap";
import { type DirectorySort, directoryRows, MINI_WEEKS, openItemText, weekLevel, workstreamWeeks } from "./workstreams";

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

describe("directoryRows", () => {
  const page = (id: string, changed: string | null, uses: number): BriefPage => ({
    id,
    title: id,
    aliases: [],
    changed,
    uses,
    lastUse: null,
  });
  const GROUPS: BriefPageGroup[] = [
    { type: "decision", pages: [page("a/one", "2026-11-09", 3), page("a/two", "2026-11-14", 9)] },
    { type: "incident", pages: [page("a/three", null, 9), page("a/four", "2026-11-12", 1)] },
  ];
  const ids = (sort: DirectorySort) => directoryRows(GROUPS, sort).map((row) => row.page.id);

  it("keeps the brief's grouping by type, each page with its group's type", () => {
    expect(directoryRows(GROUPS, "type").map((row) => [row.type, row.page.id])).toEqual([
      ["decision", "a/one"],
      ["decision", "a/two"],
      ["incident", "a/three"],
      ["incident", "a/four"],
    ]);
  });

  it("sorts by the day changed, newest first and pages without one last", () => {
    expect(ids("changed")).toEqual(["a/two", "a/four", "a/one", "a/three"]);
  });

  it("sorts by uses, most first, keeping the brief's order on a tie", () => {
    expect(ids("uses")).toEqual(["a/two", "a/three", "a/one", "a/four"]);
  });
});

describe("openItemText", () => {
  it("drops a leading list marker and keeps the rest as written", () => {
    expect(openItemText("- 离线状态报告不显示额外监听。")).toBe("离线状态报告不显示额外监听。");
    expect(openItemText("* 星号")).toBe("星号");
    expect(openItemText("2. 编号")).toBe("编号");
    expect(openItemText("没有标记 - 中间的横线不动")).toBe("没有标记 - 中间的横线不动");
  });
});
