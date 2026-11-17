import { matchRoutes } from "react-router";
import { describe, expect, it } from "vitest";
import {
  activityParams,
  activityPathOf,
  activityQueryOf,
  ALL_LEVELS,
  BACKUPS_ANCHOR,
  CANDIDATES_ANCHOR,
  dashboardPathOf,
  DEFAULT_PROMPT,
  EMPTY_SEARCH,
  filtersOf,
  logDateOf,
  logPathOf,
  logsPathOf,
  pageFilterOf,
  pageFilterParams,
  pagePathOf,
  pagesPathOf,
  personaPathOf,
  PROMPT_NAMES,
  promptNameOf,
  promptPathOf,
  searchFieldsOf,
  searchOf,
  searchParamsOf,
  settingsPathOf,
  workstreamPathOf,
  isCalendarDate,
  LOG_ROUTE,
  PAGE_ROUTE,
  pageOf,
  PROMPT_ROUTE,
  WORKSTREAM_ROUTE,
  workstreamNameOf,
  memoryPathOf,
} from "./routes";

/** The parameters the router reads from `path` for the route `pattern`, decoded as the router decodes them. */
function paramsOf(pattern: string, path: string) {
  const match = matchRoutes([{ path: pattern }], new URL(path, "http://localhost").pathname)?.at(-1);
  if (match === undefined) {
    throw new Error(`${path} does not match ${pattern}`);
  }
  return match.params;
}

describe("promptNameOf", () => {
  it("names nothing for a missing, unknown or differently cased address", () => {
    for (const name of [undefined, "", "tools", "Rules", "rules.md"]) {
      expect(promptNameOf({ name })).toBeUndefined();
    }
  });
});

describe("promptPathOf", () => {
  it("gives the address that names the document again", () => {
    for (const name of PROMPT_NAMES) {
      expect(promptNameOf(paramsOf(PROMPT_ROUTE, promptPathOf(name)))).toBe(name);
    }
  });

  it("opens the core rules by default", () => {
    expect(promptPathOf(DEFAULT_PROMPT)).toBe("/prompts/instructions");
  });
});

describe("isCalendarDate", () => {
  it.each(["2026-11-14", "2024-02-29"])("accepts the calendar date %s", (value) => {
    expect(isCalendarDate(value)).toBe(true);
  });

  it.each(["", "20261114", "2026-13-01", "2026-02-30", "2025-02-29", "2026-11-99", "today", "2026-11-14x"])(
    "refuses %s",
    (value) => {
      expect(isCalendarDate(value)).toBe(false);
    },
  );
});

describe("logDateOf", () => {
  it("reads back the day a log's address names, and nothing from one that names no day", () => {
    expect(logDateOf(paramsOf(LOG_ROUTE, logPathOf("2026-11-14", "2026-11-17")))).toBe("2026-11-14");
    expect(logDateOf(paramsOf(LOG_ROUTE, "/logs/2026-02-30"))).toBeUndefined();
  });
});

describe("logPathOf", () => {
  it("leaves today out of the address", () => {
    expect(logPathOf("2026-11-14", "2026-11-14")).toBe("/logs");
  });

  it("names any other day", () => {
    expect(logPathOf("2026-11-13", "2026-11-14")).toBe("/logs/2026-11-13");
  });
});

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

describe("searchFieldsOf", () => {
  it("reads every condition and value from the address", () => {
    const fields = searchFieldsOf(
      new URLSearchParams(
        "q=部署&workstream=ops&from=2026-11-01&to=2026-11-30&kind=pages&min=0.05&topK=3&candidates=20",
      ),
    );

    expect(fields).toEqual({
      query: "部署",
      workstream: "ops",
      from: "2026-11-01",
      to: "2026-11-30",
      kind: "pages",
      minRerankScore: "0.05",
      topK: "3",
      candidates: "20",
    });
  });

  it("ignores values it does not know", () => {
    const fields = searchFieldsOf(
      new URLSearchParams("q=部署&kind=persona&from=2026-02-31&to=2026-13-01&min=abc&topK=1.5&candidates=-2&x=1"),
    );

    expect(fields).toEqual({ ...EMPTY_SEARCH, query: "部署" });
  });
});

describe("searchParamsOf", () => {
  it("writes only the conditions that are set, and reads back the same", () => {
    const fields = { ...EMPTY_SEARCH, query: "部署", kind: "activities" as const, topK: "2" };

    const params = searchParamsOf(fields);

    expect(params.toString()).toBe(new URLSearchParams({ q: "部署", kind: "activities", topK: "2" }).toString());
    expect(searchFieldsOf(params)).toEqual(fields);
  });
});

describe("activityQueryOf", () => {
  it("reads the month, the day and the workstream from the address, the month falling back to the current one", () => {
    expect(activityQueryOf(new URLSearchParams("month=2026-10&day=2026-10-05&workstream=my-app"), "2026-11")).toEqual({
      month: "2026-10",
      day: "2026-10-05",
      workstream: "my-app",
    });
    expect(activityQueryOf(new URLSearchParams("month=latest&day=x"), "2026-11")).toEqual({
      month: "2026-11",
      day: null,
      workstream: null,
    });
  });

  it("ignores a day that is not on the calendar", () => {
    expect(activityQueryOf(new URLSearchParams("month=2026-11&day=2026-11-99"), "2026-11").day).toBeNull();
  });

  it("takes the month from a day given alone", () => {
    expect(activityQueryOf(new URLSearchParams("day=2026-09-30"), "2026-11").month).toBe("2026-09");
  });

  it("writes only what is set", () => {
    expect(activityParams({ month: "2026-10", day: null, workstream: "my-app" }).toString()).toBe(
      "month=2026-10&workstream=my-app",
    );
  });
});

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

describe("pagePathOf", () => {
  it("keeps the id's slash and encodes the section's anchor", () => {
    expect(pagePathOf("web-ui/sticky-bar")).toBe("/memory/pages/web-ui/sticky-bar");
    expect(pagePathOf("web-ui/sticky-bar", "修复 #2")).toBe(
      "/memory/pages/web-ui/sticky-bar#section-%E4%BF%AE%E5%A4%8D%20%232",
    );
  });

  it("encodes each part of the id, so the page it names is read back", () => {
    expect(pagePathOf("ops/a#b?c")).toBe("/memory/pages/ops/a%23b%3Fc");
    expect(pageOf(paramsOf(PAGE_ROUTE, pagePathOf("运维/回滚 #2")))).toEqual({
      id: "运维/回滚 #2",
      workstream: "运维",
      slug: "回滚 #2",
    });
  });
});

describe("workstreamPathOf", () => {
  it("encodes the workstream's name as one segment, which is read back", () => {
    expect(workstreamPathOf("tacit-memory")).toBe("/memory/workstreams/tacit-memory");
    expect(workstreamPathOf("a/b c")).toBe("/memory/workstreams/a%2Fb%20c");
    expect(workstreamNameOf(paramsOf(WORKSTREAM_ROUTE, workstreamPathOf("a/b c")))).toBe("a/b c");
    expect(workstreamNameOf(paramsOf(memoryPathOf("workstreams"), memoryPathOf("workstreams")))).toBeNull();
  });
});

describe("settingsPathOf", () => {
  it("opens the page, or a section by its anchor", () => {
    expect(settingsPathOf()).toBe("/settings");
    expect(settingsPathOf("bindings")).toBe("/settings#settings-bindings");
  });
});

describe("logsPathOf", () => {
  it("names the levels shown, and nothing for every level", () => {
    expect(logsPathOf({ levels: new Set(["warning", "error"]), query: "" })).toBe("/logs?level=warning%2Cerror");
    expect(logsPathOf({ levels: ALL_LEVELS, query: "" })).toBe("/logs");
  });
});

describe("pagesPathOf and activityPathOf", () => {
  it("leave the query out when nothing is set", () => {
    expect(activityPathOf({})).toBe("/memory/activity");
  });

  it("keep a workstream in the address", () => {
    expect(pagesPathOf({ workstream: "ops" })).toBe("/memory/pages?workstream=ops");
    expect(pagesPathOf()).toBe("/memory/pages");
    expect(activityPathOf({ workstream: "tacit memory" })).toBe("/memory/activity?workstream=tacit+memory");
    expect(activityPathOf({ month: "2026-11", day: "2026-11-17", workstream: null })).toBe(
      "/memory/activity?month=2026-11&day=2026-11-17",
    );
  });
});

describe("personaPathOf and dashboardPathOf", () => {
  it("lead to their cards by anchor", () => {
    expect(personaPathOf(CANDIDATES_ANCHOR)).toBe("/memory/persona#candidates");
    expect(dashboardPathOf(BACKUPS_ANCHOR)).toBe("/dashboard#backups");
  });
});
