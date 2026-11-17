import { describe, expect, it } from "vitest";
import type { PageListItem, PageType } from "../../api/memory";
import { filterPages, sectionLabel, sortPages } from "./pages";

const page = (id: string, title: string, aliases: string[] = [], type: PageType | null = "work"): PageListItem => ({
  id,
  workstream: id.split("/")[0] ?? "",
  title,
  aliases,
  type,
  writtenType: null,
  first: null,
  last: null,
  changed: null,
  uses: 0,
});

const PAGES = [
  page("ops/rollback", "部署回滚", ["回滚方案"], "decision"),
  page("ops/deploy", "Deploy pipeline", ["发布流水线"], "entity"),
  page("_topics/pgroonga", "PGroonga", [], "entity"),
  page("app/login", "登录", [], null),
];

describe("filterPages", () => {
  it("keeps the pages of the workstream and the type", () => {
    expect(filterPages(PAGES, { workstream: "ops", type: "", q: "" }).map((p) => p.id)).toEqual([
      "ops/rollback",
      "ops/deploy",
    ]);
    expect(filterPages(PAGES, { workstream: "", type: "entity", q: "" }).map((p) => p.id)).toEqual([
      "ops/deploy",
      "_topics/pgroonga",
    ]);
  });

  it("matches the keyword in the title or an alias, ignoring case", () => {
    expect(filterPages(PAGES, { workstream: "", type: "", q: "流水线" }).map((p) => p.id)).toEqual(["ops/deploy"]);
    expect(filterPages(PAGES, { workstream: "", type: "", q: "  DEPLOY " }).map((p) => p.id)).toEqual(["ops/deploy"]);
    expect(filterPages(PAGES, { workstream: "", type: "", q: "方案" }).map((p) => p.id)).toEqual(["ops/rollback"]);
  });
});

describe("sortPages", () => {
  const dated = (id: string, changed: string | null, uses: number): PageListItem => ({
    ...page(id, id),
    changed,
    uses,
  });
  const ROWS = [dated("ops/a", "2026-11-02", 3), dated("app/b", null, 5), dated("ops/c", "2026-11-09", 3)];

  it("orders by workstream in name order, with the topics last, keeping each workstream's order", () => {
    expect(sortPages(PAGES, "workstream").map((p) => p.id)).toEqual([
      "app/login",
      "ops/rollback",
      "ops/deploy",
      "_topics/pgroonga",
    ]);
  });

  it("orders by the day changed, newest first, with the undated last", () => {
    expect(sortPages(ROWS, "changed").map((p) => p.id)).toEqual(["ops/c", "ops/a", "app/b"]);
  });

  it("orders by uses, most first, keeping the order of ties", () => {
    expect(sortPages(ROWS, "uses").map((p) => p.id)).toEqual(["app/b", "ops/a", "ops/c"]);
  });
});

describe("sectionLabel", () => {
  const labels = { lesson: "教训", overturned: "推翻记录", leftovers: "遗留" };

  it("labels the three recognized sections, ignoring case as the service does", () => {
    expect(["Lesson", "overturned", "LEFTOVERS"].map((name) => sectionLabel(name, labels))).toEqual([
      "教训",
      "推翻记录",
      "遗留",
    ]);
  });

  it("shows any other section as written, a recognized name in another language included", () => {
    expect(["现状", "教训", "Lessons", "Fix"].map((name) => sectionLabel(name, labels))).toEqual([
      "现状",
      "教训",
      "Lessons",
      "Fix",
    ]);
  });
});
