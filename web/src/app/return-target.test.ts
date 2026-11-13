import { describe, expect, it } from "vitest";
import { destinationAfterLogin, routerPathOf, safeReturnTarget, targetOf } from "./return-target";

describe("safeReturnTarget", () => {
  it.each(["/ui/settings", "/ui/settings?tab=access", "/ui/logs#today", "/ui/"])("accepts the in-site path %s", (value) => {
    expect(safeReturnTarget(value)).toBe(value);
  });

  it.each([
    ["an absolute URL to another site", "https://evil.example/ui/settings"],
    ["a protocol-relative URL", "//evil.example/ui/settings"],
    ["a protocol-relative URL behind the base", "/ui//evil.example"],
    ["a backslash that browsers read as a slash", "/ui/\\evil.example"],
    ["a path outside /ui/", "/settings"],
    ["the login page's own base without the slash", "/uisettings"],
    ["a path that only starts like /ui", "/uix/settings"],
    ["a path with a control character", "/ui/settings\nSet-Cookie: x"],
    ["an empty string", ""],
  ])("rejects %s", (_label, value) => {
    expect(safeReturnTarget(value)).toBeNull();
  });

  it.each([null, undefined, 42, { path: "/ui/settings" }])("rejects a value that is not a string: %j", (value) => {
    expect(safeReturnTarget(value)).toBeNull();
  });
});

describe("routerPathOf", () => {
  it("drops the /ui base, leaving the path the router matches", () => {
    expect(routerPathOf("/ui/settings?tab=access#x")).toBe("/settings?tab=access#x");
  });

  it("maps the base itself to the root path", () => {
    expect(routerPathOf("/ui/")).toBe("/");
  });
});

describe("targetOf", () => {
  it("gives the browser path of a router location, under the base", () => {
    expect(targetOf({ pathname: "/settings", search: "?tab=access", hash: "#x" })).toBe("/ui/settings?tab=access#x");
  });

  it("gives the base with its slash for the root location", () => {
    expect(targetOf({ pathname: "/", search: "", hash: "" })).toBe("/ui/");
  });

  it("round-trips through routerPathOf", () => {
    const target = targetOf({ pathname: "/logs", search: "", hash: "" });
    expect(safeReturnTarget(target)).toBe(target);
    expect(routerPathOf(target)).toBe("/logs");
  });
});

describe("destinationAfterLogin", () => {
  it("goes to the accepted return target, as a router path", () => {
    expect(destinationAfterLogin({ from: "/ui/settings" })).toBe("/settings");
  });

  it("goes to the dashboard without a return target", () => {
    expect(destinationAfterLogin(undefined)).toBe("/dashboard");
    expect(destinationAfterLogin({})).toBe("/dashboard");
    expect(destinationAfterLogin({ from: "https://evil.example/ui/settings" })).toBe("/dashboard");
  });

  it("never returns to the login page itself", () => {
    expect(destinationAfterLogin({ from: "/ui/login" })).toBe("/dashboard");
    expect(destinationAfterLogin({ from: "/ui/login?next=/ui/x" })).toBe("/dashboard");
  });
});
