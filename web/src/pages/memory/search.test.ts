import { describe, expect, it } from "vitest";
import { EMPTY_SEARCH, requestOf, searchFieldsOf, searchParamsOf } from "./search";

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
      new URLSearchParams("q=部署&kind=persona&from=昨天&to=2026-13-01&min=abc&topK=1.5&candidates=-2&x=1"),
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

describe("requestOf", () => {
  it("keeps the query as typed, leaves out empty conditions and turns values into numbers", () => {
    expect(
      requestOf({ ...EMPTY_SEARCH, query: " 部署 ", workstream: "", minRerankScore: "0.1", candidates: "40" }),
    ).toEqual({
      query: " 部署 ",
      workstream: null,
      from: null,
      to: null,
      kind: null,
      minRerankScore: 0.1,
      topK: null,
      candidates: 40,
    });
  });
});
