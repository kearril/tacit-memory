import type { PromptName } from "../../api/prompts";

/** The file each document lives in, under `prompts/`. */
export const FILE_NAMES: Readonly<Record<PromptName, string>> = {
  instructions: "instructions.md",
  rules: "rules.md",
  resident: "resident.md",
  skill: "skill.md",
};
