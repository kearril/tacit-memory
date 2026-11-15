import { useMemo } from "react";
import { type MdBlock, type MdInline, parseMarkdown } from "./markdown";
import { SourceNote } from "./parts";

/**
 * A page section's Markdown, typeset. It is read into blocks first, so no HTML in the text ever reaches the page; the
 * sources a comment records show as small muted notes. Loaded on its own, with the parser, when a page is opened.
 */
export default function MarkdownBody({ text }: { readonly text: string }) {
  const blocks = useMemo(() => parseMarkdown(text), [text]);
  return (
    <div className="grid gap-sm leading-(--tk-line-height-normal)">
      <Blocks blocks={blocks} />
    </div>
  );
}

function Blocks({ blocks }: { readonly blocks: readonly MdBlock[] }) {
  return blocks.map((block, index) => <Block key={index} block={block} />);
}

/** Heading sizes under a section's own heading: a page's sections are the second level, so their headings start at the third. */
const HEADING_CLASSES = ["text-base font-semibold", "text-sm font-semibold", "text-sm font-semibold text-ink-muted"];

function Block({ block }: { readonly block: MdBlock }) {
  switch (block.type) {
    case "paragraph":
      return (
        <p className="break-words">
          <Inlines inlines={block.inlines} />
        </p>
      );
    case "heading": {
      const Tag = block.level <= 3 ? "h3" : block.level === 4 ? "h4" : "h5";
      return (
        <Tag className={`mt-sm ${HEADING_CLASSES[Math.min(Math.max(block.level - 3, 0), 2)]}`}>
          <Inlines inlines={block.inlines} />
        </Tag>
      );
    }
    case "list": {
      const items = block.items.map((item, index) => (
        <li key={index} className={item.task === null ? "pl-xs" : "flex list-none items-start gap-sm"}>
          {item.task === null ? null : (
            <input type="checkbox" checked={item.task} readOnly disabled className="mt-xs accent-(--tk-color-accent)" />
          )}
          <div className="grid min-w-0 gap-xs">
            <Blocks blocks={item.blocks} />
          </div>
        </li>
      ));
      return block.ordered ? (
        <ol start={block.start ?? undefined} className="grid list-decimal gap-xs pl-lg marker:text-ink-muted">
          {items}
        </ol>
      ) : (
        <ul className="grid list-disc gap-xs pl-lg marker:text-ink-muted">{items}</ul>
      );
    }
    case "quote":
      return (
        <blockquote className="grid gap-sm border-l-(length:--tk-border-thick) border-line pl-md text-ink-muted">
          <Blocks blocks={block.blocks} />
        </blockquote>
      );
    case "code":
      return (
        <pre className="overflow-x-auto rounded-md bg-surface-raised p-md font-mono text-xs leading-(--tk-line-height-normal)">
          <code>{block.text}</code>
        </pre>
      );
    case "html":
      return <p className="font-mono text-xs break-words whitespace-pre-wrap text-ink-muted">{block.text}</p>;
    case "source":
      return <SourceNote source={block.source} />;
    case "rule":
      return <hr className="border-line" />;
    case "table":
      return (
        <div className="overflow-x-auto">
          <table className="w-full border-collapse text-sm">
            <thead>
              <tr>
                {block.header.map((cell, index) => (
                  <th
                    key={index}
                    className="border-b-(length:--tk-border-thin) border-line px-sm py-xs text-left font-semibold"
                  >
                    <Inlines inlines={cell} />
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {block.rows.map((row, rowIndex) => (
                <tr key={rowIndex}>
                  {row.map((cell, index) => (
                    <td key={index} className="border-b-(length:--tk-border-thin) border-line px-sm py-xs align-top">
                      <Inlines inlines={cell} />
                    </td>
                  ))}
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      );
  }
}

function Inlines({ inlines }: { readonly inlines: readonly MdInline[] }) {
  return inlines.map((inline, index) => <Inline key={index} inline={inline} />);
}

function Inline({ inline }: { readonly inline: MdInline }) {
  switch (inline.type) {
    case "text":
      return inline.text;
    case "strong":
      return (
        <strong className="font-semibold">
          <Inlines inlines={inline.children} />
        </strong>
      );
    case "em":
      return (
        <em>
          <Inlines inlines={inline.children} />
        </em>
      );
    case "strike":
      return (
        <s className="text-ink-muted">
          <Inlines inlines={inline.children} />
        </s>
      );
    case "code":
      return <code className="rounded-sm bg-surface-raised px-xs font-mono text-sm">{inline.text}</code>;
    case "link":
      return inline.href === null ? (
        <Inlines inlines={inline.children} />
      ) : (
        <a
          href={inline.href}
          target="_blank"
          rel="noreferrer noopener"
          className="text-accent underline-offset-2 hover:underline"
        >
          <Inlines inlines={inline.children} />
        </a>
      );
    case "break":
      return <br />;
    case "source":
      return <SourceNote source={inline.source} inline />;
  }
}
