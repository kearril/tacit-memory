import { describe, expect, it } from "vitest";
import { en } from "../i18n/en";
import { zh } from "../i18n/zh";
import { pageTitleOf } from "./page-title";

describe("pageTitleOf", () => {
  it("names top-level pages in the memory language and falls back to the app name", () => {
    expect(pageTitleOf("/dashboard", zh)).toBe("仪表盘 · TacitMemory");
    expect(pageTitleOf("/settings", zh)).toBe("设置 · TacitMemory");
    expect(pageTitleOf("/login", zh)).toBe("登录 · TacitMemory");
    expect(pageTitleOf("/logs", zh)).toBe("日志 · TacitMemory");
    expect(pageTitleOf("/dashboard", en)).toBe("Dashboard · TacitMemory");
    expect(pageTitleOf("/login", en)).toBe("Log in · TacitMemory");
    expect(pageTitleOf("/", zh)).toBe("TacitMemory");
    expect(pageTitleOf("/unknown", en)).toBe("TacitMemory");
  });

  it("names prompt documents and falls back to the prompts section for unknown document names", () => {
    expect(pageTitleOf("/prompts/instructions", zh)).toBe("核心规则 · TacitMemory");
    expect(pageTitleOf("/prompts/skill", en)).toBe("Writing conventions · TacitMemory");
    expect(pageTitleOf("/prompts", zh)).toBe("提示词 · TacitMemory");
    expect(pageTitleOf("/prompts/other", en)).toBe("Prompts · TacitMemory");
  });

  it("includes a valid calendar date for dated log views and ignores invalid dates", () => {
    expect(pageTitleOf("/logs/2026-11-18", zh)).toBe("2026-11-18 · 日志 · TacitMemory");
    expect(pageTitleOf("/logs/2026-11-18", en)).toBe("2026-11-18 · Logs · TacitMemory");
    expect(pageTitleOf("/logs/not-a-date", zh)).toBe("日志 · TacitMemory");
  });

  it("names memory views and front-loads workstream or detail page identities", () => {
    expect(pageTitleOf("/memory", zh)).toBe("记忆 · TacitMemory");
    expect(pageTitleOf("/memory/persona", zh)).toBe("画像 · TacitMemory");
    expect(pageTitleOf("/memory/workstreams", zh)).toBe("主线 · TacitMemory");
    expect(pageTitleOf("/memory/pages", zh)).toBe("细节页 · TacitMemory");
    expect(pageTitleOf("/memory/activity", en)).toBe("Activity · TacitMemory");
    expect(pageTitleOf("/memory/search", en)).toBe("Search · TacitMemory");
    expect(pageTitleOf("/memory/workstreams/tacit-memory", zh)).toBe("tacit-memory · 主线 · TacitMemory");
    expect(pageTitleOf("/memory/workstreams/mac%20mini", en)).toBe("mac mini · Workstreams · TacitMemory");
    expect(pageTitleOf("/memory/pages/ops/rollback", zh)).toBe("ops/rollback · 细节页 · TacitMemory");
    expect(pageTitleOf("/memory/pages/_topics/%E9%83%A8%E7%BD%B2", en)).toBe("_topics/部署 · Pages · TacitMemory");
  });
});
