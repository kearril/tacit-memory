import type { PromptName } from "../../api/prompts";

/** The four prompt documents in the order the sidebar lists them, which is the order the service gives them. */
export const PROMPT_NAMES = ["instructions", "rules", "resident", "skill"] as const satisfies readonly PromptName[];

/** The document `/prompts` opens. */
export const DEFAULT_PROMPT: PromptName = "instructions";

/** The file each document lives in, under `prompts/`. */
export const FILE_NAMES: Readonly<Record<PromptName, string>> = {
  instructions: "instructions.md",
  rules: "rules.md",
  resident: "resident.md",
  skill: "skill.md",
};

/** The section headings `resident.md` accepts, in their order; any other heading is refused. */
export const RESIDENT_SECTIONS = [
  "red_lines",
  "working_rules",
  "preferences",
  "profile",
  "situational_rules",
  "workstreams",
] as const;

/** The path of a document's page. */
export function promptPathOf(name: PromptName): string {
  return `/prompts/${name}`;
}

/** The document a page address names; `undefined` for anything else, which the page sends on to the default document. */
export function promptNameOf(param: string | undefined): PromptName | undefined {
  return PROMPT_NAMES.find((name) => name === param);
}
