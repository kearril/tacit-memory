import { describe, expect, it } from "vitest";
import { DEFAULT_PROMPT, PROMPT_NAMES, promptNameOf, promptPathOf } from "./names";

describe("promptNameOf", () => {
  it("names each of the four documents by its address", () => {
    for (const name of PROMPT_NAMES) {
      expect(promptNameOf(name)).toBe(name);
    }
  });

  it("names nothing for a missing, unknown or differently cased address", () => {
    expect(promptNameOf(undefined)).toBeUndefined();
    expect(promptNameOf("")).toBeUndefined();
    expect(promptNameOf("tools")).toBeUndefined();
    expect(promptNameOf("Rules")).toBeUndefined();
    expect(promptNameOf("rules.md")).toBeUndefined();
  });
});

describe("promptPathOf", () => {
  it("gives the address that names the document again", () => {
    for (const name of PROMPT_NAMES) {
      expect(promptNameOf(promptPathOf(name).split("/").at(-1))).toBe(name);
    }
  });

  it("opens the core rules by default", () => {
    expect(promptPathOf(DEFAULT_PROMPT)).toBe("/prompts/instructions");
  });
});
