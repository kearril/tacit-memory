import { describe, expect, it } from "vitest";
import type { ConfigFile, ConfigView } from "../../api/config";
import {
  bodyOf,
  changedPaths,
  differsFromDefault,
  draftsOf,
  edit,
  placeProblems,
  restoreDefault,
} from "./form";

const threshold = { certain: 0.85, unlikely: 0.25 };

function configFile(): ConfigFile {
  return {
    server: { port: null, host: null },
    memory: { path: null },
    language: "zh",
    models: { embedding: null, reranker: null, decision: null },
    limits: { brief: null, page: null },
    consolidation: {},
    decision: {},
    retrieval: {},
    backup: { keep: null, webDav: null },
  } as ConfigFile;
}

/** A view whose file sets `topK` to 3 and the same-page thresholds, with the service's defaults beside it. */
function view(): ConfigView {
  const file = configFile();
  file.retrieval = { topK: 3 };
  file.decision = { samePage: { certain: 0.9, unlikely: 0.3 } };
  const defaults = configFile();
  defaults.retrieval = { candidates: 30, topK: 5, minRerankScore: 0.001 };
  defaults.decision = { samePreference: threshold, samePage: threshold, sameTopic: threshold, ruleConflict: threshold };
  return { hash: "h", file, running: file, defaults, keys: [] };
}

describe("draftsOf", () => {
  it("shows each setting as the file holds it, and the default where the file leaves it out", () => {
    const drafts = draftsOf(view());

    expect(drafts["retrieval.topK"]).toBe("3");
    expect(drafts["retrieval.candidates"]).toBe("30");
    expect(drafts["retrieval.minRerankScore"]).toBe("0.001");
    expect(drafts["decision.samePage.certain"]).toBe("0.9");
    expect(drafts["decision.sameTopic.unlikely"]).toBe("0.25");
  });
});

describe("changedPaths", () => {
  it("lists nothing before an edit", () => {
    expect(changedPaths(view(), draftsOf(view()))).toEqual([]);
  });

  it("lists each edited setting once, and not one edited back to the same number", () => {
    let drafts = draftsOf(view());
    drafts = edit(drafts, "retrieval.topK", "4");
    drafts = edit(drafts, "decision.samePage.unlikely", "0.2");
    drafts = edit(drafts, "retrieval.candidates", "30.0");

    expect(changedPaths(view(), drafts)).toEqual(["retrieval.topK", "decision.samePage.unlikely"]);
  });

  it("counts restoring the default as a change of each setting it moves", () => {
    const drafts = restoreDefault(view(), draftsOf(view()), "decision.samePage");

    expect(changedPaths(view(), drafts)).toEqual(["decision.samePage.certain", "decision.samePage.unlikely"]);
  });
});

describe("differsFromDefault and restoreDefault", () => {
  it("marks a setting or a pair of thresholds that differs from its default, and restores it", () => {
    const drafts = draftsOf(view());

    expect(differsFromDefault(view(), drafts, "retrieval.topK")).toBe(true);
    expect(differsFromDefault(view(), drafts, "retrieval.candidates")).toBe(false);
    expect(differsFromDefault(view(), drafts, "decision.samePage")).toBe(true);
    expect(differsFromDefault(view(), drafts, "decision.sameTopic")).toBe(false);

    const restored = restoreDefault(view(), restoreDefault(view(), drafts, "retrieval.topK"), "decision.samePage");
    expect(restored["retrieval.topK"]).toBe("5");
    expect(restored["decision.samePage.certain"]).toBe("0.85");
    expect(differsFromDefault(view(), restored, "decision.samePage")).toBe(false);
  });
});

describe("bodyOf", () => {
  it("leaves out a setting equal to its default, so the file follows the default from then on", () => {
    let drafts = restoreDefault(view(), draftsOf(view()), "retrieval.topK");
    drafts = edit(drafts, "retrieval.candidates", "40");

    const { config, invalid } = bodyOf(view(), drafts);

    expect(invalid).toEqual([]);
    expect(config.retrieval).toEqual({ candidates: 40, topK: null, minRerankScore: null });
  });

  it("writes both thresholds of a pair once either differs from its default, and none when both are the default", () => {
    let drafts = edit(draftsOf(view()), "decision.sameTopic.certain", "0.8");
    drafts = restoreDefault(view(), drafts, "decision.samePage");

    const { config } = bodyOf(view(), drafts);

    expect(config.decision.sameTopic).toEqual({ certain: 0.8, unlikely: 0.25 });
    expect(config.decision.samePage).toBeNull();
    expect(config.decision.samePreference).toBeNull();
  });

  it("takes an emptied field as its default", () => {
    const { config } = bodyOf(view(), edit(draftsOf(view()), "retrieval.topK", " "));

    expect(config.retrieval.topK).toBeNull();
  });

  it("keeps the sections the page does not edit as the file holds them", () => {
    const original = view();
    original.file.backup = { keep: 2, webDav: null };

    const { config } = bodyOf(original, draftsOf(original));

    expect(config.backup).toEqual({ keep: 2, webDav: null });
    expect(config.language).toBe("zh");
  });

  it("names the fields that are not a number, or not a whole number where one is needed", () => {
    let drafts = edit(draftsOf(view()), "retrieval.topK", "2.5");
    drafts = edit(drafts, "decision.samePage.certain", "abc");
    drafts = edit(drafts, "retrieval.minRerankScore", "0.01");

    expect(bodyOf(view(), drafts).invalid).toEqual(["retrieval.topK", "decision.samePage.certain"]);
  });
});

describe("placeProblems", () => {
  it("places a problem on its field, a problem of a pair on the pair, and counts them by section", () => {
    const placed = placeProblems([
      { path: "decision.samePage", message: "order" },
      { path: "retrieval.topK", message: "range" },
      { path: "decision.sameTopic.certain", message: "probability" },
    ]);

    expect(placed.byField).toEqual({
      "decision.samePage": ["order"],
      "retrieval.topK": ["range"],
      "decision.sameTopic.certain": ["probability"],
    });
    expect(placed.bySection).toEqual({ judgment: 3 });
    expect(placed.unplaced).toEqual([]);
  });

  it("keeps the problems no field on the page answers to apart, in the order given", () => {
    const placed = placeProblems([
      { path: "backup.keep", message: "keep" },
      { path: "decision", message: "unknown key" },
      { path: "retrieval.topK", message: "range" },
    ]);

    expect(placed.unplaced.map((problem) => problem.path)).toEqual(["backup.keep", "decision"]);
    expect(placed.bySection).toEqual({ judgment: 1 });
  });

  it("gives the first problem in the page's order, to scroll to", () => {
    const placed = placeProblems([
      { path: "decision.ruleConflict.unlikely", message: "a" },
      { path: "retrieval.candidates", message: "b" },
    ]);

    expect(placed.first).toBe("retrieval.candidates");
  });
});
