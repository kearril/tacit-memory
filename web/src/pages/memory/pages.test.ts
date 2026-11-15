import { describe, expect, it } from "vitest";
import type { PageListItem } from "../../api/memory";
import { filterPages, groupPages, pageFilterOf, pageFilterParams } from "./pages";

const page = (id: string, title: string, aliases: string[] = [], type: string | null = "work"): PageListItem => ({
  id,
  workstream: id.split("/")[0] ?? "",
  title,
  aliases,
  type,
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

describe("pageFilterOf and pageFilterParams", () => {
  it("read the workstream, the type and the keyword, ignoring a type that is not one", () => {
    expect(pageFilterOf(new URLSearchParams("workstream=ops&type=decision&q=回滚"))).toEqual({
      workstream: "ops",
      type: "decision",
      q: "回滚",
    });
    expect(pageFilterOf(new URLSearchParams("type=nonsense&other=1"))).toEqual({ workstream: "", type: "", q: "" });
  });

  it("write only what is set, and read back the same", () => {
    const filter = { workstream: "", type: "entity" as const, q: "pipe" };
    const params = pageFilterParams(filter);
    expect(params.toString()).toBe("type=entity&q=pipe");
    expect(pageFilterOf(params)).toEqual(filter);
  });
});

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

describe("groupPages", () => {
  it("groups by workstream in name order, with the topics last", () => {
    expect(groupPages(PAGES).map((group) => [group.workstream, group.pages.map((p) => p.id)])).toEqual([
      ["app", ["app/login"]],
      ["ops", ["ops/rollback", "ops/deploy"]],
      ["_topics", ["_topics/pgroonga"]],
    ]);
  });
});
