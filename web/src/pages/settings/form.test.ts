import { describe, expect, it } from "vitest";
import type { ConfigFile, ConfigKey, ConfigKeyPath, ConfigView } from "../../api/config";
import {
  bodyOf,
  changedPaths,
  differsFromDefault,
  drifted,
  draftsOf,
  edit,
  ON,
  outOfRange,
  pendingRestart,
  placeProblems,
  restoreDefault,
  restartsAmong,
} from "./form";

const threshold = { certain: 0.85, unlikely: 0.25 };

/** A key as the service describes it: a plain one unless told otherwise. */
function key(path: ConfigKeyPath, shape: Partial<ConfigKey> = {}): ConfigKey {
  return { path, min: null, max: null, choices: null, restart: false, readOnly: false, secret: false, ...shape };
}

/** The keys the service describes apart from the other fields: the read-only ones and the secrets. */
const APART: ConfigKey[] = [
  key("memory.path", { restart: true, readOnly: true }),
  key("language", { choices: ["zh", "en"], restart: true, readOnly: true }),
  key("models.embedding.apiKey", { secret: true }),
  key("models.reranker.apiKey", { secret: true }),
  key("models.decision.apiKey", { secret: true }),
  key("backup.webdav.password", { secret: true }),
];

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
  return { hash: "h", file, running: file, defaults, keys: APART, secrets: [] };
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
    const placed = placeProblems(view(), [
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
    const placed = placeProblems(view(), [
      { path: "unknown.key", message: "unknown" },
      { path: "decision", message: "unknown key" },
      { path: "retrieval.topK", message: "range" },
    ]);

    expect(placed.unplaced.map((problem) => problem.path)).toEqual(["unknown.key", "decision"]);
    expect(placed.bySection).toEqual({ judgment: 1 });
  });

  it("gives the first problem in the page's order, to scroll to", () => {
    const placed = placeProblems(view(), [
      { path: "decision.ruleConflict.unlikely", message: "a" },
      { path: "retrieval.candidates", message: "b" },
    ]);

    expect(placed.first).toBe("retrieval.candidates");
  });
});

/** A view of a service started with port 18888 and no extra listener, whose file now asks for others. */
function general(): ConfigView {
  const base = view();
  const keys = [
    key("server.port", { min: 1, max: 65535, restart: true }),
    key("server.host", { restart: true }),
    key("language", { choices: ["zh", "en"], restart: true, readOnly: true }),
    key("retrieval.topK", { min: 1, max: 100000 }),
    key("retrieval.candidates", { min: 1, max: 100000 }),
  ];
  const running = structuredClone(base.file);
  running.server = { port: 18888, host: null };
  running.retrieval = { candidates: 30, topK: 3, minRerankScore: null };
  const file = structuredClone(base.file);
  file.server = { port: 19000, host: "0.0.0.0" };
  file.retrieval = { candidates: 30, topK: 3, minRerankScore: null };
  base.defaults.server = { port: 18888, host: "127.0.0.1" };
  return { ...base, file, running, keys };
}

describe("pendingRestart", () => {
  it("lists each setting saved with a value the service takes only on a restart, with both values", () => {
    expect(pendingRestart(general())).toEqual([
      { path: "server.port", running: "18888", saved: "19000" },
      { path: "server.host", running: "127.0.0.1", saved: "0.0.0.0" },
    ]);
  });

  it("lists nothing once the service runs what the file holds", () => {
    const settled = general();
    settled.running = structuredClone(settled.file);

    expect(pendingRestart(settled)).toEqual([]);
  });
});

describe("drifted", () => {
  it("names a setting that takes effect at once but runs with another value than the file holds", () => {
    const edited = general();
    edited.file.retrieval = { ...edited.file.retrieval, topK: 7 };

    expect(drifted(edited)).toEqual(["retrieval.topK"]);
  });

  it("names nothing when only settings that wait for a restart differ", () => {
    expect(drifted(general())).toEqual([]);
  });
});

describe("text settings", () => {
  it("leaves out an address equal to its default, and keeps another as typed but trimmed", () => {
    const base = general();

    expect(bodyOf(base, edit(draftsOf(base), "server.host", "127.0.0.1")).config.server.host).toBeNull();
    expect(bodyOf(base, edit(draftsOf(base), "server.host", " 192.168.1.5 ")).config.server.host).toBe("192.168.1.5");
    expect(changedPaths(base, edit(draftsOf(base), "server.host", "0.0.0.0 "))).toEqual([]);
  });
});

describe("secrets", () => {
  it("are sent only when changed, and count as changes whether rewritten or removed", () => {
    const secrets = { "backup.webdav.password": "s3cret", "models.decision.apiKey": null };
    const modelled = withDecision();

    expect(bodyOf(modelled, draftsOf(modelled)).secrets).toEqual({});
    expect(bodyOf(modelled, draftsOf(modelled), secrets).secrets).toEqual(secrets);
    expect(changedPaths(modelled, draftsOf(modelled), secrets)).toEqual([
      "models.decision.apiKey",
      "backup.webdav.password",
    ]);
  });

  it("are not rewritten to nothing", () => {
    expect(bodyOf(view(), draftsOf(view()), { "backup.webdav.password": " " }).invalid).toEqual([
      "backup.webdav.password",
    ]);
  });

  it("place the service's problems on their own fields", () => {
    expect(placeProblems(view(), [{ path: "backup.webdav.password", message: "required" }]).bySection).toEqual({
      backup: 1,
    });
  });
});

describe("cloud backup", () => {
  it("is none once both its address and its user name are emptied, and is written once either is given", () => {
    const configured = view();
    configured.file.backup = {
      keep: null,
      webDav: { url: "https://dav.example/tacit/", username: null, password: null },
    };

    let drafts = edit(draftsOf(configured), "backup.webdav.url", "");
    expect(bodyOf(configured, drafts).config.backup.webDav).toBeNull();

    drafts = edit(draftsOf(view()), "backup.webdav.url", "https://dav.example/other/");
    expect(bodyOf(view(), drafts).config.backup.webDav).toMatchObject({
      url: "https://dav.example/other/",
      username: null,
    });
  });
});

/** A view whose file configures the decision model. */
function withDecision(): ConfigView {
  const base = view();
  base.file.models = {
    ...base.file.models,
    decision: { provider: null, url: "http://127.0.0.1:9/judge", model: "judge", apiKey: null },
  };
  return base;
}

describe("model toggles", () => {
  it("leave a model switched off out of the config, and keep what was typed for it on the page", () => {
    let drafts = edit(draftsOf(withDecision()), "models.decision.model", "judge-2");
    drafts = edit(drafts, "models.decision", "");

    const { config } = bodyOf(withDecision(), drafts);

    expect(config.models.decision).toBeNull();
    expect(drafts["models.decision.model"]).toBe("judge-2");
    expect(changedPaths(withDecision(), drafts)).toEqual(["models.decision"]);
    expect(drafts["models.decision"]).not.toBe(ON);
  });

  it("write a model switched on with what its inputs hold, the generic protocol as no provider", () => {
    let drafts = edit(draftsOf(view()), "models.embedding", ON);
    drafts = edit(drafts, "models.embedding.url", "https://api.example/v1/embeddings");
    drafts = edit(drafts, "models.embedding.model", "embed-3");

    const { config } = bodyOf(view(), drafts);

    expect(config.models.embedding).toMatchObject({
      provider: null,
      url: "https://api.example/v1/embeddings",
      model: "embed-3",
    });
    expect(changedPaths(view(), drafts)).toEqual([
      "models.embedding",
      "models.embedding.url",
      "models.embedding.model",
    ]);
  });

  it("leave out the secret of a model switched off", () => {
    const drafts = edit(draftsOf(withDecision()), "models.decision", "");

    expect(bodyOf(withDecision(), drafts, { "models.decision.apiKey": "k" }).secrets).toEqual({});
  });
});

describe("outOfRange", () => {
  it("gives the limits of a number typed outside them, and nothing for one inside, an empty input or a half-typed number", () => {
    let drafts = edit(draftsOf(general()), "retrieval.topK", "0");
    expect(outOfRange(general(), drafts, "retrieval.topK")).toEqual({ min: 1, max: 100000 });

    drafts = edit(drafts, "retrieval.topK", "7");
    expect(outOfRange(general(), drafts, "retrieval.topK")).toBeNull();
    drafts = edit(drafts, "retrieval.topK", "");
    expect(outOfRange(general(), drafts, "retrieval.topK")).toBeNull();
    drafts = edit(drafts, "retrieval.topK", "1e");
    expect(outOfRange(general(), drafts, "retrieval.topK")).toBeNull();
  });

  it("gives nothing for a setting without limits", () => {
    const drafts = edit(draftsOf(general()), "server.host", "anything");
    expect(outOfRange(general(), drafts, "server.host")).toBeNull();
  });
});

describe("restartsAmong", () => {
  it("counts the changed settings that take effect only when the service restarts", () => {
    expect(restartsAmong(general(), ["server.port", "server.host", "retrieval.topK"])).toBe(2);
    expect(restartsAmong(general(), ["retrieval.topK"])).toBe(0);
  });
});
