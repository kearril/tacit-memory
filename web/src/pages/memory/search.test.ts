import { describe, expect, it } from "vitest";
import { requestOf } from "./search";
import { EMPTY_SEARCH } from "../../lib/routes";

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
