import { describe, expect, it } from "vitest";
import type { PersonaEntryView, PersonaReport, PersonaSectionView } from "../../api/memory";
import { occurrenceDaysOf, personaScopes, personaSections, scopeKeyOf } from "./persona";

const entry = (line: number, text: string, code: string | null = `c${line}`): PersonaEntryView => ({
  line,
  code,
  text,
  lines: [],
  sources: [],
});

const section = (kind: PersonaSectionView["section"], ...entries: PersonaEntryView[]): PersonaSectionView => ({
  section: kind,
  heading: kind === null ? "杂项" : null,
  entries,
});

const FILE = { path: "persona.md", problems: [] };

const REPORT: PersonaReport = {
  personaFile: FILE,
  global: [
    section("redLines"),
    section("workingRules", entry(3, "先讨论再动手")),
    section("preferences", entry(5, "回答简短")),
    section("profile"),
  ],
  domains: [
    {
      name: "network-proxy",
      line: 10,
      trigger: "改代理时",
      sections: [section("workingRules", entry(12, "不拿单次测速当结论")), section(null, entry(14, "随手记", null))],
    },
    { name: "release-notes", line: 20, trigger: null, sections: [section("preferences", entry(22, "精炼"))] },
  ],
  workstreams: [
    { name: "tacit-memory", line: 30, trigger: null, sections: [section("preferences", entry(32, "不微调"))] },
  ],
  candidatesFile: FILE,
  candidates: [],
  rejectionList: [],
};

describe("personaScopes", () => {
  it("lists global, each domain and each workstream in the file's order, with their entry counts", () => {
    expect(personaScopes(REPORT).map(({ key, kind, name, count }) => ({ key, kind, name, count }))).toEqual([
      { key: "global", kind: "global", name: null, count: 2 },
      { key: "domain:network-proxy", kind: "domain", name: "network-proxy", count: 1 },
      { key: "domain:release-notes", kind: "domain", name: "release-notes", count: 1 },
      { key: "workstream:tacit-memory", kind: "workstream", name: "tacit-memory", count: 1 },
    ]);
  });

  it("carries a domain's trigger, which is null when it is missing", () => {
    const scopes = personaScopes(REPORT);
    expect(scopes[1]?.trigger).toBe("改代理时");
    expect(scopes[2]?.trigger).toBeNull();
  });
});

describe("personaSections", () => {
  it("gathers every scope's entries under the four sections in order, each with its scope", () => {
    const sections = personaSections(REPORT, null);
    expect(sections.map((group) => group.section)).toEqual([
      "redLines",
      "workingRules",
      "preferences",
      "profile",
      null,
    ]);
    expect(sections[1]?.rows.map((row) => [row.scope.key, row.entry.text])).toEqual([
      ["global", "先讨论再动手"],
      ["domain:network-proxy", "不拿单次测速当结论"],
    ]);
    expect(sections[2]?.rows.map((row) => row.scope.key)).toEqual([
      "global",
      "domain:release-notes",
      "workstream:tacit-memory",
    ]);
    expect(sections[0]?.rows).toEqual([]);
  });

  it("puts what sits under no known section last, with the heading it is under", () => {
    const other = personaSections(REPORT, null).at(-1);
    expect(other?.section).toBeNull();
    expect(other?.rows.map((row) => [row.heading, row.entry.text])).toEqual([["杂项", "随手记"]]);
  });

  it("leaves the other group out when nothing is in it", () => {
    expect(personaSections(REPORT, "global").map((group) => group.section)).toEqual([
      "redLines",
      "workingRules",
      "preferences",
      "profile",
    ]);
  });

  it("keeps only one scope's entries when filtered by it", () => {
    const sections = personaSections(REPORT, "domain:network-proxy");
    expect(sections.flatMap((group) => group.rows).map((row) => row.entry.text)).toEqual([
      "不拿单次测速当结论",
      "随手记",
    ]);
  });
});

describe("scopeKeyOf", () => {
  it("accepts a scope the persona has and ignores any other", () => {
    expect(scopeKeyOf(new URLSearchParams("scope=domain:network-proxy"), REPORT)).toBe("domain:network-proxy");
    expect(scopeKeyOf(new URLSearchParams("scope=global"), REPORT)).toBe("global");
    expect(scopeKeyOf(new URLSearchParams("scope=domain:gone"), REPORT)).toBeNull();
    expect(scopeKeyOf(new URLSearchParams(""), REPORT)).toBeNull();
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
