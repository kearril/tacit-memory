import { describe, expect, it } from "vitest";
import type { PromptDocument } from "../../api/prompts";
import { stateOf } from "./editor";

const measure = (text: string): PromptDocument["measure"] => ({ length: text.length, limit: 3000, activityRoom: null, items: null });
const builtIn = (name: PromptDocument["name"], text: string): PromptDocument => ({
  name,
  text,
  builtIn: true,
  hash: null,
  measure: measure(text),
  problem: null,
});
const file = (name: PromptDocument["name"], text: string): PromptDocument => ({
  name,
  text,
  builtIn: false,
  hash: "a".repeat(64),
  measure: measure(text),
  problem: null,
});

describe("stateOf", () => {
  it("keeps a built-in document read-only until customizing starts", () => {
    const document = builtIn("rules", "## 值得记\n- 一条\n");

    expect(stateOf(document, false, document.text)).toEqual({ readOnly: true, dirty: false, canSave: false, canRestore: false, invalid: false });
  });

  it("lets a built-in document be saved as it is once customizing starts, so its text becomes the file", () => {
    const document = builtIn("instructions", "## 核心\n");

    expect(stateOf(document, true, document.text)).toEqual({ readOnly: false, dirty: false, canSave: true, canRestore: false, invalid: false });
    expect(stateOf(document, true, "## 核心\n- 新\n")).toEqual({ readOnly: false, dirty: true, canSave: true, canRestore: false, invalid: false });
  });

  it("lets the empty built-in writing conventions be written at once, and saved only once something is written", () => {
    const document = builtIn("skill", "");

    expect(stateOf(document, false, "")).toEqual({ readOnly: false, dirty: false, canSave: false, canRestore: false, invalid: false });
    expect(stateOf(document, false, "页面标题用中文。")).toEqual({ readOnly: false, dirty: true, canSave: true, canRestore: false, invalid: false });
  });

  it("lets a customized document be edited, saved when changed, and returned to its default", () => {
    const document = file("resident", "外壳\n");

    expect(stateOf(document, false, document.text)).toEqual({ readOnly: false, dirty: false, canSave: false, canRestore: true, invalid: false });
    expect(stateOf(document, false, "外壳，改过\n")).toEqual({ readOnly: false, dirty: true, canSave: true, canRestore: true, invalid: false });
  });

  it("marks a document whose file the service does not run, which stays editable so it can be fixed", () => {
    const document: PromptDocument = { ...file("resident", "外壳\n\n## mainline\n"), problem: "第 3 行的 ## mainline 不是小节名" };

    expect(stateOf(document, false, document.text)).toEqual({ readOnly: false, dirty: false, canSave: false, canRestore: true, invalid: true });
  });
});
