import { describe, expect, it } from "vitest";
import { toggledOverride } from "./appearance";

describe("toggledOverride", () => {
  it("pins the other scheme while it differs from the system's", () => {
    expect(toggledOverride("light", "light")).toBe("dark");
    expect(toggledOverride("dark", "dark")).toBe("light");
  });

  it("goes back to following the system rather than pinning the system's own scheme", () => {
    expect(toggledOverride("dark", "light")).toBeNull();
    expect(toggledOverride("light", "dark")).toBeNull();
  });
});
