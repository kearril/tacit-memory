import type { PromptDocument } from "../../api/prompts";

/** What the editor of one document allows, from the document as the service holds it and what the user has done. */
export interface EditorState {
  /** The text cannot be typed into: the built-in text of a document whose customizing has not started. */
  readonly readOnly: boolean;
  /** The text differs from the document's; leaving asks first. */
  readonly dirty: boolean;
  /** Saving would write the file. Once customizing starts, the built-in text may be saved unchanged, to start the file from it. */
  readonly canSave: boolean;
  /** There is a file to delete, so the built-in text can apply again. */
  readonly canRestore: boolean;
  /** The file breaks its rules, so the service keeps running the document's previous text. */
  readonly invalid: boolean;
}

/**
 * The state of the editor of `document` with the text `draft`; `customizing` says the user chose to start a file from the
 * built-in text. The writing conventions are empty by default, so they need no such choice and are written into at once.
 */
export function stateOf(document: PromptDocument, customizing: boolean, draft: string): EditorState {
  const readOnly = document.builtIn && document.name !== "skill" && !customizing;
  const dirty = draft !== document.text;
  return {
    readOnly,
    dirty,
    canSave: !readOnly && (dirty || (document.builtIn && customizing)),
    canRestore: !document.builtIn,
    invalid: document.problem !== null,
  };
}
