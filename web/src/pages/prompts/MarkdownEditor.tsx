import { defaultKeymap, history, historyKeymap } from "@codemirror/commands";
import { markdown } from "@codemirror/lang-markdown";
import { HighlightStyle, syntaxHighlighting } from "@codemirror/language";
import { Annotation, Compartment, EditorState } from "@codemirror/state";
import { EditorView, keymap, lineNumbers, placeholder as placeholderText } from "@codemirror/view";
import { tags } from "@lezer/highlight";
import { useEffect, useRef } from "react";
import { lf } from "./editor";

/**
 * The colours of the Markdown a prompt document is written in. Headings, the marks that shape the text, comments (which
 * are not sent to the agent), inline code and links each take a colour of their own; every colour is a design token, so
 * the dark theme has its own.
 */
const promptHighlight = HighlightStyle.define([
  {
    tag: [tags.heading1, tags.heading2, tags.heading3, tags.heading4, tags.heading5, tags.heading6],
    color: "var(--tk-color-syntax-heading)",
    fontWeight: "var(--tk-font-weight-semibold)",
  },
  { tag: tags.processingInstruction, color: "var(--tk-color-syntax-mark)" },
  { tag: tags.comment, color: "var(--tk-color-syntax-comment)", fontStyle: "italic" },
  {
    tag: tags.monospace,
    color: "var(--tk-color-syntax-code)",
    backgroundColor: "var(--tk-color-surface-raised)",
    borderRadius: "var(--tk-radius-xs)",
  },
  { tag: [tags.link, tags.url], color: "var(--tk-color-syntax-link)" },
  { tag: tags.strong, fontWeight: "var(--tk-font-weight-semibold)" },
  { tag: tags.emphasis, fontStyle: "italic" },
  { tag: tags.quote, color: "var(--tk-color-ink-muted)", fontStyle: "italic" },
]);

/**
 * The editor's look in the design tokens: the monospace font, a gutter of quiet line numbers, the least height of a
 * prompt editor, and no scrolling of its own, as it grows with its text. The frame and focus ring are the wrapper's.
 */
const promptTheme = EditorView.theme({
  "&": { color: "var(--tk-color-ink)", backgroundColor: "transparent", fontSize: "var(--tk-font-size-sm)" },
  "&.cm-focused": { outline: "none" },
  ".cm-scroller": {
    fontFamily: "var(--tk-font-mono)",
    lineHeight: "var(--tk-line-height-normal)",
    overflow: "visible",
  },
  ".cm-content": {
    minHeight: "var(--tk-size-editor)",
    padding: "var(--tk-space-md) 0",
    caretColor: "var(--tk-color-ink)",
  },
  ".cm-line": { padding: "0 var(--tk-space-md)" },
  ".cm-gutters": {
    backgroundColor: "transparent",
    color: "var(--tk-color-ink-muted)",
    borderRight: "var(--tk-border-thin) solid var(--tk-color-line)",
  },
  ".cm-lineNumbers .cm-gutterElement": { padding: "0 var(--tk-space-sm) 0 var(--tk-space-md)" },
  ".cm-placeholder": { color: "var(--tk-color-ink-muted)" },
  ".cm-cursor": { borderLeftColor: "var(--tk-color-ink)" },
  "&.cm-focused .cm-selectionBackground, .cm-selectionBackground, .cm-content ::selection": {
    backgroundColor: "var(--tk-color-accent-soft)",
  },
});

/** Whether the text can be typed into, as one setting that changes without a new editor. */
function editable(readOnly: boolean) {
  return [EditorState.readOnly.of(readOnly), EditorView.editable.of(!readOnly)];
}

/** Marks a change that came from `value`, which is not the user's typing and so is not reported back. */
const fromValue = Annotation.define<boolean>();

/**
 * The text of a prompt document in a Markdown editor: coloured by its syntax, numbered by line, growing with its text,
 * with undo. `value` is the text; a new one from outside (a discard, a reload) replaces what the editor holds, while
 * what the user types comes back through `onChange`. `focused` hands the editor to the keyboard when it turns true.
 */
export default function MarkdownEditor({
  value,
  onChange,
  readOnly,
  label,
  placeholder,
  focused,
}: {
  readonly value: string;
  readonly onChange: (text: string) => void;
  readonly readOnly: boolean;
  readonly label: string;
  readonly placeholder: string;
  readonly focused: boolean;
}) {
  const host = useRef<HTMLDivElement>(null);
  const view = useRef<EditorView | null>(null);
  const access = useRef(new Compartment());
  // The editor is made once; these hold what its first state needs and what its listener calls, as they are now.
  const initial = useRef({ value, readOnly, label, placeholder });
  const changed = useRef(onChange);
  useEffect(() => {
    changed.current = onChange;
  }, [onChange]);

  useEffect(() => {
    const parent = host.current;
    if (parent === null) {
      return;
    }
    const start = initial.current;
    const editor = new EditorView({
      parent,
      state: EditorState.create({
        doc: start.value,
        extensions: [
          lineNumbers(),
          history(),
          keymap.of([...defaultKeymap, ...historyKeymap]),
          markdown(),
          syntaxHighlighting(promptHighlight),
          EditorView.lineWrapping,
          placeholderText(start.placeholder),
          access.current.of(editable(start.readOnly)),
          EditorView.contentAttributes.of({ "aria-label": start.label, spellcheck: "false" }),
          EditorView.updateListener.of((update) => {
            if (update.docChanged && !update.transactions.some((transaction) => transaction.annotation(fromValue))) {
              changed.current(update.state.doc.toString());
            }
          }),
          promptTheme,
        ],
      }),
    });
    view.current = editor;
    return () => {
      editor.destroy();
      view.current = null;
    };
  }, []);

  useEffect(() => {
    const editor = view.current;
    const text = lf(value);
    if (editor !== null && editor.state.doc.toString() !== text) {
      editor.dispatch({
        changes: { from: 0, to: editor.state.doc.length, insert: text },
        annotations: fromValue.of(true),
      });
    }
  }, [value]);

  useEffect(() => {
    view.current?.dispatch({ effects: access.current.reconfigure(editable(readOnly)) });
  }, [readOnly]);

  useEffect(() => {
    if (focused) {
      view.current?.focus();
    }
  }, [focused]);

  return (
    <div
      ref={host}
      data-readonly={readOnly ? "" : undefined}
      className="w-full overflow-hidden rounded-md border-(length:--tk-border-thin) border-line bg-surface focus-within:border-accent focus-within:ring-(length:--tk-border-thick) focus-within:ring-accent data-readonly:bg-surface-raised"
    />
  );
}
