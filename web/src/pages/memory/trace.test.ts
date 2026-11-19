import { describe, expect, it } from "vitest";
import type { TraceUnit, UnitFate } from "../../api/memory";
import { scoreText, segmentsOf } from "./trace";

function unit(fate: UnitFate, fusedRank: number): TraceUnit {
  return {
    kind: "section",
    source: "ops/rollback",
    section: `小节 ${fusedRank}`,
    code: null,
    date: null,
    workstream: "ops",
    text: "",
    fullTextRank: fusedRank,
    bm25: -1,
    vectorRank: null,
    distance: null,
    rrf: 0.01,
    fusedRank,
    rerankScore: null,
    fate,
    result: fate === "shown" ? 1 : null,
  };
}

describe("segmentsOf", () => {
  it("lists the four fates in pipeline order with their units, even when a fate has none", () => {
    const segments = segmentsOf([
      unit("beyondCandidates", 4),
      unit("shown", 1),
      unit("belowThreshold", 3),
      unit("shown", 2),
    ]);

    expect(segments.map((segment) => segment.fate)).toEqual([
      "shown",
      "beyondTopK",
      "belowThreshold",
      "beyondCandidates",
    ]);
    expect(segments.map((segment) => segment.units.length)).toEqual([2, 0, 1, 1]);
    expect(segments[0]?.units.map((item) => item.fusedRank)).toEqual([1, 2]);
  });

  it("gives each segment its share of all units", () => {
    const segments = segmentsOf([
      unit("shown", 1),
      unit("belowThreshold", 2),
      unit("belowThreshold", 3),
      unit("beyondCandidates", 4),
    ]);

    expect(segments.map((segment) => segment.share)).toEqual([0.25, 0, 0.5, 0.25]);
  });

  it("counts a unit whose page could not be read with those beyond topK", () => {
    const segments = segmentsOf([unit("unreadable", 1), unit("beyondTopK", 2)]);

    expect(segments[1]?.units.map((item) => item.fate)).toEqual(["unreadable", "beyondTopK"]);
  });

  it("has no share when there are no units", () => {
    expect(segmentsOf([]).map((segment) => segment.share)).toEqual([0, 0, 0, 0]);
  });

  it("does not mutate the input array", () => {
    const input = [unit("beyondCandidates", 4), unit("shown", 1), unit("belowThreshold", 3)];
    segmentsOf(input);
    expect(input.map((item) => item.fusedRank)).toEqual([4, 1, 3]);
  });
});

describe("scoreText", () => {
  it("keeps two significant digits and writes scores below 0.001 in exponent form", () => {
    expect([0.912, 0.0412, 0.00123, 0.000066, 1, 0].map(scoreText)).toEqual([
      "0.91",
      "0.041",
      "0.0012",
      "6.6e-5",
      "1",
      "0",
    ]);
  });
});
