import { describe, expect, it } from "vitest";
import type { PromptMeasure } from "../../api/prompts";
import { zh } from "../../i18n/zh";
import { measureLines } from "./measure";

const words = zh.prompts.measure;

describe("measureLines", () => {
  it("gives a document with a limit of its own its characters against that limit", () => {
    const measure: PromptMeasure = { length: 1200, limit: 1500, activityRoom: null, items: null };

    expect(measureLines(measure, words)).toEqual([{ text: "1200 / 1500 字符", over: false }]);
  });

  it("marks a document past its limit", () => {
    const measure: PromptMeasure = { length: 3001, limit: 3000, activityRoom: null, items: null };

    expect(measureLines(measure, words)).toEqual([{ text: "3001 / 3000 字符", over: true }]);
  });

  it("gives the core rules the fixed part and the room left for recent activity", () => {
    const measure: PromptMeasure = { length: 2222, limit: 3000, activityRoom: 1778, items: null };

    expect(measureLines(measure, words)).toEqual([
      { text: "服务端说明固定部分 2222 / 3000 字符", over: false },
      { text: "最近活动还能用 1778 字符", over: false },
    ]);
  });

  it("gives the criteria the items of each section as well, marking a full one only when past its limit", () => {
    const measure: PromptMeasure = {
      length: 2100,
      limit: 3000,
      activityRoom: 1900,
      items: { worthRecording: 10, notRecorded: 11, limit: 10 },
    };

    expect(measureLines(measure, words).slice(2)).toEqual([
      { text: "值得记 10 / 10 条", over: false },
      { text: "不记 11 / 10 条", over: true },
    ]);
  });

  it("says as it is when the fixed part leaves no room for recent activity", () => {
    const measure: PromptMeasure = { length: 4120, limit: 3000, activityRoom: -120, items: null };

    expect(measureLines(measure, words)).toEqual([
      { text: "服务端说明固定部分 4120 / 3000 字符", over: true },
      { text: "最近活动没有空间，还差 120 字符", over: true },
    ]);
  });
});
