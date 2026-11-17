import { describe, expect, it } from "vitest";
import type { SourceCommentView } from "../../api/memory";
import { parseMarkdown } from "./markdown";

/** The source comments of a section as the service gives them: each comment as written, and what it records. */
const sources = (...found: [comment: string, date: string | null, quote: string | null][]): SourceCommentView[] =>
  found.map(([comment, date, quote]) => ({ comment, source: { date, quote } }));

describe("parseMarkdown", () => {
  it("shows a comment on its own line as the source the service read from it, and drops any other comment", () => {
    expect(
      parseMarkdown(
        "失败先回滚。\n\n<!-- 2026-11-01 原话「先回滚」 -->\n\n<!-- 备注 -->",
        sources(["<!-- 2026-11-01 原话「先回滚」 -->", "2026-11-01", "先回滚"]),
      ),
    ).toEqual([
      { type: "paragraph", inlines: [{ type: "text", text: "失败先回滚。" }] },
      { type: "source", source: { date: "2026-11-01", quote: "先回滚" } },
    ]);
  });

  it("shows a comment within a line as its source, found by the comment as written", () => {
    expect(
      parseMarkdown(
        "最快止损<!-- quote「stop the loss」 -->。",
        sources(["<!-- quote「stop the loss」 -->", null, "stop the loss"]),
      ),
    ).toEqual([
      {
        type: "paragraph",
        inlines: [
          { type: "text", text: "最快止损" },
          { type: "source", source: { date: null, quote: "stop the loss" } },
          { type: "text", text: "。" },
        ],
      },
    ]);
  });

  it("drops a comment the service did not read as a source, however it looks", () => {
    expect(parseMarkdown("正文\n\n<!-- 2026-11-01 原话「看着像」 -->", [])).toEqual([
      { type: "paragraph", inlines: [{ type: "text", text: "正文" }] },
    ]);
  });

  it("reads a source comment under a list item as a source of that item", () => {
    expect(
      parseMarkdown(
        "- 蓝绿部署\n  <!-- 2026-11-02 原话「用蓝绿」 -->",
        sources(["<!-- 2026-11-02 原话「用蓝绿」 -->", "2026-11-02", "用蓝绿"]),
      ),
    ).toEqual([
      {
        type: "list",
        ordered: false,
        start: null,
        items: [
          {
            task: null,
            blocks: [
              { type: "paragraph", inlines: [{ type: "text", text: "蓝绿部署" }] },
              { type: "source", source: { date: "2026-11-02", quote: "用蓝绿" } },
            ],
          },
        ],
      },
    ]);
  });

  it("keeps HTML as text, never as markup", () => {
    expect(parseMarkdown("<script>alert(1)</script>", [])).toEqual([
      { type: "html", text: "<script>alert(1)</script>" },
    ]);
    expect(parseMarkdown("a <b>b</b>", [])).toEqual([
      { type: "paragraph", inlines: [{ type: "text", text: "a <b>b</b>" }] },
    ]);
  });

  it("reads emphasis, code, links and their targets, keeping only safe ones", () => {
    expect(parseMarkdown("**粗** `代码` [文档](https://example.com) [坏](javascript:alert(1))", [])).toEqual([
      {
        type: "paragraph",
        inlines: [
          { type: "strong", children: [{ type: "text", text: "粗" }] },
          { type: "text", text: " " },
          { type: "code", text: "代码" },
          { type: "text", text: " " },
          { type: "link", href: "https://example.com", children: [{ type: "text", text: "文档" }] },
          { type: "text", text: " " },
          { type: "link", href: null, children: [{ type: "text", text: "坏" }] },
        ],
      },
    ]);
  });

  it("joins the lines of a paragraph without a space between Chinese characters", () => {
    expect(parseMarkdown("第一行\n第二行\nand English\nwords", [])).toEqual([
      { type: "paragraph", inlines: [{ type: "text", text: "第一行第二行 and English words" }] },
    ]);
  });

  it("reads headings, fenced code, quotes, task lists and tables", () => {
    const blocks = parseMarkdown(
      "### 细节\n\n```bash\ngit push\n```\n\n> 引用\n\n- [x] 完成\n\n| a | b |\n| - | - |\n| 1 | 2 |",
      [],
    );
    expect(blocks.map((block) => block.type)).toEqual(["heading", "code", "quote", "list", "table"]);
    expect(blocks[0]).toEqual({ type: "heading", level: 3, inlines: [{ type: "text", text: "细节" }] });
    expect(blocks[1]).toEqual({ type: "code", info: "bash", text: "git push" });
    expect(blocks[3]).toMatchObject({ items: [{ task: true }] });
    expect(blocks[4]).toEqual({
      type: "table",
      header: [[{ type: "text", text: "a" }], [{ type: "text", text: "b" }]],
      rows: [[[{ type: "text", text: "1" }], [{ type: "text", text: "2" }]]],
    });
  });
});
