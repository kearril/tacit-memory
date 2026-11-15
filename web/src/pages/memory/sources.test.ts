import { describe, expect, it } from "vitest";
import { occurrenceDaysOf, sourceOf, splitSources } from "./sources";

describe("sourceOf", () => {
  it("reads the day and the quote of a source comment", () => {
    expect(sourceOf("<!-- 2026-10-25 原话「别删库」 -->")).toEqual({ date: "2026-10-25", quote: "别删库" });
    expect(sourceOf("<!-- 2026-10-25 quote「don't drop it」 -->")).toEqual({
      date: "2026-10-25",
      quote: "don't drop it",
    });
  });

  it("reads a comment with a day and no quote, or a quote and no day", () => {
    expect(sourceOf("<!-- 2026-10-25 -->")).toEqual({ date: "2026-10-25", quote: null });
    expect(sourceOf("<!-- 原话「先问」 -->")).toEqual({ date: null, quote: "先问" });
  });

  it("keeps the brackets of a quote that holds them", () => {
    expect(sourceOf("<!-- 2026-10-25 原话「用「」括起来」 -->")?.quote).toBe("用「」括起来");
  });

  it("is null for a line that is not a comment, or a comment that is neither", () => {
    expect(sourceOf("先列出不清楚的点")).toBeNull();
    expect(sourceOf("<!-- 随手的备注 -->")).toBeNull();
  });
});

describe("splitSources", () => {
  it("parts an entry's continuation into its text lines and its sources, in order", () => {
    expect(
      splitSources([
        "先列出不清楚的点",
        "<!-- 2026-10-20 原话「先问清楚」 -->",
        "<!-- 2026-10-28 原话「还是要先问」 -->",
      ]),
    ).toEqual({
      lines: ["先列出不清楚的点"],
      sources: [
        { date: "2026-10-20", quote: "先问清楚" },
        { date: "2026-10-28", quote: "还是要先问" },
      ],
    });
  });

  it("drops a comment that is not a source, as the entry's text leaves it out", () => {
    expect(splitSources(["<!-- 随手的备注 -->"])).toEqual({ lines: [], sources: [] });
  });
});

describe("occurrenceDaysOf", () => {
  it("gives each day once, in order, marked rejected when any record of it is", () => {
    expect(
      occurrenceDaysOf([
        { date: "2026-11-01", quote: "a", rejected: false },
        { date: "2026-11-03", quote: "b", rejected: false },
        { date: "2026-11-01", quote: "c", rejected: false },
        { date: "2026-11-03", quote: "d", rejected: true },
      ]),
    ).toEqual([
      { date: "2026-11-01", rejected: false },
      { date: "2026-11-03", rejected: true },
    ]);
  });
});
