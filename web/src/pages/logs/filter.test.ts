import { describe, expect, it } from "vitest";
import type { ServiceLogEntry } from "../../api/logs";
import { ALL_LEVELS, applyFilters, filtersOf, highlightParts, levelCounts, searchOf } from "./filter";

const entry = (level: ServiceLogEntry["level"], message: string): ServiceLogEntry => ({ time: null, level, message });

const ENTRIES: readonly ServiceLogEntry[] = [
  entry("information", "工具 recall：完成"),
  entry("warning", "额外监听绑定失败"),
  entry("information", "工具 write_page：已写入"),
  entry("error", "备份失败\nSystem.IO.IOException: Recall disk full"),
];

describe("filtersOf", () => {
  it("shows every level and no keyword by default", () => {
    expect(filtersOf(new URLSearchParams())).toEqual({ levels: ALL_LEVELS, query: "" });
  });

  it("reads the levels and the keyword", () => {
    const filters = filtersOf(new URLSearchParams("level=warning,error&q=recall"));
    expect([...filters.levels]).toEqual(["warning", "error"]);
    expect(filters.query).toBe("recall");
  });

  it("ignores levels it does not know, and shows every level when none is left", () => {
    expect([...filtersOf(new URLSearchParams("level=warning,debug")).levels]).toEqual(["warning"]);
    expect(filtersOf(new URLSearchParams("level=debug")).levels).toEqual(ALL_LEVELS);
  });
});

describe("searchOf", () => {
  it("leaves the defaults out", () => {
    expect(searchOf({ levels: ALL_LEVELS, query: "" })).toBe("");
  });

  it("writes the levels in their order and the keyword", () => {
    expect(searchOf({ levels: new Set(["error", "warning"]), query: "re call" })).toBe(
      "?level=warning%2Cerror&q=re+call",
    );
  });

  it("reads back what it writes", () => {
    const filters = { levels: new Set(["information", "error"] as const), query: "备份" };
    const read = filtersOf(new URLSearchParams(searchOf(filters)));
    expect([...read.levels]).toEqual(["information", "error"]);
    expect(read.query).toBe("备份");
  });
});

describe("applyFilters", () => {
  it("keeps the chosen levels", () => {
    expect(applyFilters(ENTRIES, { levels: new Set(["warning", "error"]), query: "" }).map((e) => e.level)).toEqual([
      "warning",
      "error",
    ]);
  });

  it("matches the keyword in any case, in the lines that follow the first", () => {
    expect(applyFilters(ENTRIES, { levels: ALL_LEVELS, query: "RECALL" }).map((e) => e.message.slice(0, 4))).toEqual([
      "工具 r",
      "备份失败",
    ]);
  });

  it("takes the keyword literally", () => {
    expect(
      applyFilters([entry("information", "a.b"), entry("information", "axb")], { levels: ALL_LEVELS, query: "a.b" }),
    ).toHaveLength(1);
  });

  it("ignores a keyword of spaces only", () => {
    expect(applyFilters(ENTRIES, { levels: ALL_LEVELS, query: "  " })).toHaveLength(4);
  });
});

describe("levelCounts", () => {
  it("counts each level, the absent ones as 0", () => {
    expect(levelCounts(ENTRIES.slice(0, 3))).toEqual({ information: 2, warning: 1, error: 0 });
  });
});

describe("highlightParts", () => {
  it("is the whole text when there is no keyword", () => {
    expect(highlightParts("abc", "")).toEqual([{ text: "abc", hit: false }]);
  });

  it("marks every match in any case, keeping the text as written", () => {
    expect(highlightParts("Recall then recall", "RECALL")).toEqual([
      { text: "Recall", hit: true },
      { text: " then ", hit: false },
      { text: "recall", hit: true },
    ]);
  });

  it("takes special characters literally and finds them across lines", () => {
    expect(highlightParts("x(1)\ny(1)", "(1)")).toEqual([
      { text: "x", hit: false },
      { text: "(1)", hit: true },
      { text: "\ny", hit: false },
      { text: "(1)", hit: true },
    ]);
  });
});
