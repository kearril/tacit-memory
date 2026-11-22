import type { GraphOrigin, TraceUnit, UnitFate } from "../../api/memory";

/** The fates a trace view lists, in the order the pipeline decides them. */
export const SEGMENT_FATES = [
  "shown",
  "beyondTopK",
  "belowRelativeFloor",
  "beyondActivityCap",
  "belowThreshold",
  "beyondCandidates",
] as const;
export type SegmentFate = (typeof SEGMENT_FATES)[number];

/** One segment of the trace view: the units that met one fate, in fused order, and their share of all units. */
export interface Segment {
  readonly fate: SegmentFate;
  readonly units: readonly TraceUnit[];
  readonly share: number;
}

/**
 * The units of a trace by fate, one segment per fate in pipeline order, each in fused order. A unit whose page could no
 * longer be read was within topK yet not shown, which is rare enough to count with those beyond topK.
 */
export function segmentsOf(units: readonly TraceUnit[]): Segment[] {
  const ordered = units.toSorted((a, b) => a.fusedRank - b.fusedRank);
  return SEGMENT_FATES.map((fate) => {
    const members = ordered.filter((item) => segmentOf(item.fate) === fate);
    return { fate, units: members, share: units.length === 0 ? 0 : members.length / units.length };
  });
}

function segmentOf(fate: UnitFate): SegmentFate {
  return fate === "unreadable" ? "beyondTopK" : fate;
}

/** A score in two significant digits, as small scores need: 0.91, 0.041, 0.0012; below 0.001 in exponent form, 6.6e-5. */
export function scoreText(score: number): string {
  return score !== 0 && Math.abs(score) < 0.001 ? score.toExponential(1) : String(Number(score.toPrecision(2)));
}

/** Formats an optional score (such as `rerankScore` or `finalScore`) for a table cell, returning an em dash when absent. */
export function optionalScoreText(score: number | null | undefined): string {
  return score === null || score === undefined ? "—" : scoreText(score);
}

/** Label for a non-direct graph-expansion origin, or `null` when the unit matched directly. */
export function graphOriginLabel(
  origin: GraphOrigin | undefined,
  labels: { readonly supersedes: string; readonly related: string },
): string | null {
  switch (origin) {
    case "supersedes":
      return labels.supersedes;
    case "related":
      return labels.related;
    default:
      return null;
  }
}
