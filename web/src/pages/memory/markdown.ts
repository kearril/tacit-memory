import type { SyntaxNode } from "@lezer/common";
import { GFM, parser } from "@lezer/markdown";
import type { SourceCommentView, SourceView } from "../../api/memory";

/**
 * A page's Markdown read into blocks the page view renders itself. Nothing in it is HTML: HTML in the text is kept as
 * text, a comment that records a source becomes a source, and any other comment is left out, as the agent's text leaves
 * it out.
 */
export type MdBlock =
  | { readonly type: "paragraph"; readonly inlines: readonly MdInline[] }
  | { readonly type: "heading"; readonly level: number; readonly inlines: readonly MdInline[] }
  | {
      readonly type: "list";
      readonly ordered: boolean;
      readonly start: number | null;
      readonly items: readonly MdListItem[];
    }
  | { readonly type: "quote"; readonly blocks: readonly MdBlock[] }
  | { readonly type: "code"; readonly info: string; readonly text: string }
  | { readonly type: "source"; readonly source: SourceView }
  | { readonly type: "html"; readonly text: string }
  | { readonly type: "rule" }
  | {
      readonly type: "table";
      readonly header: readonly (readonly MdInline[])[];
      readonly rows: readonly (readonly (readonly MdInline[])[])[];
    };

/** A list item; `task` is whether a task item is ticked, `null` for an item that is not a task. */
export interface MdListItem {
  readonly task: boolean | null;
  readonly blocks: readonly MdBlock[];
}

export type MdInline =
  | { readonly type: "text"; readonly text: string }
  | { readonly type: "strong" | "em" | "strike"; readonly children: readonly MdInline[] }
  | { readonly type: "code"; readonly text: string }
  | { readonly type: "link"; readonly href: string | null; readonly children: readonly MdInline[] }
  | { readonly type: "break" }
  | { readonly type: "source"; readonly source: SourceView };

const markdown = parser.configure(GFM);

/** The marks of the syntax, which the blocks and inlines they belong to stand for. */
const MARKS = new Set([
  "HeaderMark",
  "EmphasisMark",
  "CodeMark",
  "LinkMark",
  "QuoteMark",
  "ListMark",
  "StrikethroughMark",
  "TableDelimiter",
  "TaskMarker",
  "CodeInfo",
  "LinkTitle",
  "LinkLabel",
  "URL",
]);

/** The link targets the view follows; any other scheme stays text. */
const SAFE_HREF = /^(?:https?:|mailto:|#|\/)/iu;

/** A character of the scripts that are written without spaces between words. */
const CJK = /[\p{Script=Han}\p{Script=Hiragana}\p{Script=Katakana}\u{3000}-\u{303f}\u{ff00}-\u{ffef}]/u;

/** The text being read, and the sources the service read from its comments, by the comment as written. */
interface Doc {
  readonly text: string;
  readonly sources: ReadonlyMap<string, SourceView>;
}

/**
 * Reads `text` into blocks. A comment is shown as a source when `sources`, the section's source comments as the service
 * read them, holds it; the view never reads a comment's words itself.
 */
export function parseMarkdown(text: string, sources: readonly SourceCommentView[]): readonly MdBlock[] {
  const doc: Doc = { text, sources: new Map(sources.map((found) => [found.comment, found.source])) };
  return blocksOf(markdown.parse(text).topNode, doc);
}

/** The source a comment records, or `null` for a comment that records none. */
function sourceOf(raw: string, doc: Doc): SourceView | null {
  return doc.sources.get(raw.trim()) ?? null;
}

function blocksOf(parent: SyntaxNode, doc: Doc): MdBlock[] {
  const blocks: MdBlock[] = [];
  for (let node = parent.firstChild; node !== null; node = node.nextSibling) {
    const block = blockOf(node, doc);
    if (block !== null) {
      blocks.push(block);
    }
  }
  return blocks;
}

function blockOf(node: SyntaxNode, doc: Doc): MdBlock | null {
  const raw = doc.text.slice(node.from, node.to);
  const heading = /^(?:ATXHeading|SetextHeading)(\d)$/u.exec(node.name);
  if (heading !== null) {
    return { type: "heading", level: Number(heading[1]), inlines: inlinesOf(node, doc) };
  }
  switch (node.name) {
    case "Paragraph":
      return { type: "paragraph", inlines: inlinesOf(node, doc) };
    case "BulletList":
    case "OrderedList":
      return listOf(node, doc);
    case "Blockquote":
      return { type: "quote", blocks: blocksOf(node, doc) };
    case "FencedCode": {
      const info = node.getChild("CodeInfo");
      const code = node.getChild("CodeText");
      return {
        type: "code",
        info: info === null ? "" : doc.text.slice(info.from, info.to),
        text: code === null ? "" : doc.text.slice(code.from, code.to),
      };
    }
    case "CodeBlock":
      return { type: "code", info: "", text: raw.replace(/^ {4}/gmu, "") };
    case "CommentBlock": {
      const source = sourceOf(raw, doc);
      return source === null ? null : { type: "source", source };
    }
    case "HTMLBlock":
    case "ProcessingInstructionBlock":
      return { type: "html", text: raw };
    case "HorizontalRule":
      return { type: "rule" };
    case "Table":
      return tableOf(node, doc);
    default:
      return null;
  }
}

function listOf(node: SyntaxNode, doc: Doc): MdBlock {
  const items: MdListItem[] = [];
  let start: number | null = null;
  for (let item = node.firstChild; item !== null; item = item.nextSibling) {
    if (item.name !== "ListItem") {
      continue;
    }
    const mark = item.getChild("ListMark");
    if (node.name === "OrderedList" && start === null && mark !== null) {
      start = Number.parseInt(doc.text.slice(mark.from, mark.to), 10);
    }
    const task = item.getChild("Task");
    if (task !== null) {
      const marker = task.getChild("TaskMarker");
      const ticked = marker !== null && /x/iu.test(doc.text.slice(marker.from, marker.to));
      items.push({
        task: ticked,
        blocks: [{ type: "paragraph", inlines: inlinesOf(task, doc) }, ...blocksOf(item, doc)],
      });
    } else {
      items.push({ task: null, blocks: blocksOf(item, doc) });
    }
  }
  return { type: "list", ordered: node.name === "OrderedList", start, items };
}

function tableOf(node: SyntaxNode, doc: Doc): MdBlock {
  const cellsOf = (row: SyntaxNode) => row.getChildren("TableCell").map((cell) => inlinesOf(cell, doc));
  const header = node.getChild("TableHeader");
  return {
    type: "table",
    header: header === null ? [] : cellsOf(header),
    rows: node.getChildren("TableRow").map(cellsOf),
  };
}

/** The inlines of a node: the text between its children, and each child read by its kind. */
function inlinesOf(parent: SyntaxNode, doc: Doc): MdInline[] {
  const inlines: MdInline[] = [];
  let at = parent.from;
  const pushText = (value: string) => {
    if (value === "") {
      return;
    }
    const last = inlines.at(-1);
    if (last?.type === "text") {
      inlines[inlines.length - 1] = { type: "text", text: last.text + value };
    } else {
      inlines.push({ type: "text", text: value });
    }
  };

  for (let node = parent.firstChild; node !== null; node = node.nextSibling) {
    pushText(doc.text.slice(at, node.from));
    at = node.to;
    const raw = doc.text.slice(node.from, node.to);
    if (MARKS.has(node.name) || node.name === "Task") {
      continue;
    }
    switch (node.name) {
      case "StrongEmphasis":
        inlines.push({ type: "strong", children: inlinesOf(node, doc) });
        break;
      case "Emphasis":
        inlines.push({ type: "em", children: inlinesOf(node, doc) });
        break;
      case "Strikethrough":
        inlines.push({ type: "strike", children: inlinesOf(node, doc) });
        break;
      case "InlineCode":
        inlines.push({ type: "code", text: raw.replace(/^`+|`+$/gu, "").trim() });
        break;
      case "Link": {
        const url = node.getChild("URL");
        const href = url === null ? null : doc.text.slice(url.from, url.to);
        inlines.push({
          type: "link",
          href: href !== null && SAFE_HREF.test(href) ? href : null,
          children: inlinesOf(node, doc),
        });
        break;
      }
      case "Autolink": {
        const href = raw.replace(/^<|>$/gu, "");
        inlines.push({
          type: "link",
          href: SAFE_HREF.test(href) ? href : null,
          children: [{ type: "text", text: href }],
        });
        break;
      }
      case "URL":
        break;
      case "HardBreak":
        inlines.push({ type: "break" });
        break;
      case "Escape":
        pushText(raw.slice(1));
        break;
      case "Comment": {
        const source = sourceOf(raw, doc);
        if (source !== null) {
          inlines.push({ type: "source", source });
        }
        break;
      }
      default:
        pushText(raw);
    }
  }
  pushText(doc.text.slice(at, parent.to));
  return joinLines(trimEnds(inlines));
}

/** Leading and trailing whitespace of the first and last text, which the block's indentation and line ends leave. */
function trimEnds(inlines: MdInline[]): MdInline[] {
  const first = inlines[0];
  if (first?.type === "text") {
    inlines[0] = { type: "text", text: first.text.trimStart() };
  }
  const last = inlines.at(-1);
  if (last?.type === "text") {
    inlines[inlines.length - 1] = { type: "text", text: last.text.trimEnd() };
  }
  return inlines.filter((inline) => inline.type !== "text" || inline.text !== "");
}

/**
 * The line breaks inside the text, as a reader sees them: a space between words, nothing between two characters of a
 * script written without spaces. The indentation of a continuation line goes with its break.
 */
function joinLines(inlines: MdInline[]): MdInline[] {
  return inlines.map((inline) =>
    inline.type !== "text"
      ? inline
      : {
          type: "text",
          text: inline.text.replace(/(.?)[ \t]*\n[ \t]*(.?)/gu, (_, before: string, after: string) =>
            before !== "" && after !== "" && CJK.test(before) && CJK.test(after)
              ? before + after
              : `${before} ${after}`,
          ),
        },
  );
}
