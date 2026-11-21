import { describe, expect, it, vi } from "vitest";
import {
  RELEASES_URL,
  REPOSITORY_URL,
  compareSemver,
  fetchLatestStableRelease,
  isNewerStableVersion,
  parseSemver,
  readCachedRelease,
  writeCachedRelease,
} from "./update-check";

describe("parseSemver", () => {
  it("parses plain, v-prefixed, prerelease, and build-metadata versions", () => {
    expect(parseSemver("1.2.3")).toEqual({ major: 1, minor: 2, patch: 3, prerelease: [] });
    expect(parseSemver("v0.1.1-alpha.0.47+cd36d27")).toEqual({
      major: 0,
      minor: 1,
      patch: 1,
      prerelease: ["alpha", "0", "47"],
    });
  });

  it("rejects malformed versions", () => {
    expect(parseSemver("")).toBeNull();
    expect(parseSemver("1.2")).toBeNull();
    expect(parseSemver("1.2.3-")).toBeNull();
    expect(parseSemver("1.2.3-alpha..1")).toBeNull();
  });
});

describe("compareSemver", () => {
  it("orders major, minor, patch, and prerelease segments according to SemVer 2.0.0", () => {
    const cmp = (a: string, b: string) => Math.sign(compareSemver(parseSemver(a)!, parseSemver(b)!));
    expect(cmp("0.2.0", "0.1.9")).toBe(1);
    expect(cmp("0.1.1", "0.1.1-alpha.0.47")).toBe(1);
    expect(cmp("0.1.1-alpha.0.47", "0.1.1-alpha.0.9")).toBe(1);
    expect(cmp("0.1.1-beta.1", "0.1.1-alpha.9")).toBe(1);
    expect(cmp("1.0.0+abc", "1.0.0+def")).toBe(0);
  });
});

describe("isNewerStableVersion", () => {
  it("detects newer stable releases for both stable and prerelease installations", () => {
    expect(isNewerStableVersion("0.1.0", "0.2.0")).toBe(true);
    expect(isNewerStableVersion("0.1.1-alpha.0.47+abc", "0.1.1")).toBe(true);
    expect(isNewerStableVersion("0.1.1-alpha.0.47", "0.1.0")).toBe(false);
    expect(isNewerStableVersion("0.2.0", "0.2.0")).toBe(false);
  });

  it("ignores prerelease targets and development placeholder builds", () => {
    expect(isNewerStableVersion("0.1.0", "0.2.0-rc.1")).toBe(false);
    expect(isNewerStableVersion("0.0.0-alpha.0", "0.2.0")).toBe(false);
    expect(isNewerStableVersion("dev", "0.2.0")).toBe(false);
  });
});

describe("cached release storage", () => {
  function memoryStorage(): Storage {
    const map = new Map<string, string>();
    return {
      get length() {
        return map.size;
      },
      clear: () => map.clear(),
      getItem: (key) => map.get(key) ?? null,
      key: (index) => [...map.keys()][index] ?? null,
      removeItem: (key) => {
        map.delete(key);
      },
      setItem: (key, value) => {
        map.set(key, value);
      },
    };
  }

  it("round-trips a cached release entry and handles corrupt JSON", () => {
    const storage = memoryStorage();
    const entry = { checkedAt: 1700000000000, latestVersion: "0.2.0", releaseUrl: `${RELEASES_URL}/tag/v0.2.0` };
    writeCachedRelease(entry, storage);
    expect(readCachedRelease(storage)).toEqual(entry);

    storage.setItem("tacit.updateCheck", "{not-json");
    expect(readCachedRelease(storage)).toBeNull();
  });
});

describe("fetchLatestStableRelease", () => {
  it("parses a valid stable release and strips the leading v", async () => {
    const mockFetch = vi.fn<typeof fetch>().mockResolvedValue(
      new Response(
        JSON.stringify({
          tag_name: "v0.2.0",
          html_url: `${REPOSITORY_URL}/releases/tag/v0.2.0`,
          draft: false,
          prerelease: false,
        }),
        { status: 200 },
      ),
    );
    await expect(fetchLatestStableRelease(undefined, mockFetch)).resolves.toEqual({
      latestVersion: "0.2.0",
      releaseUrl: `${REPOSITORY_URL}/releases/tag/v0.2.0`,
    });
  });

  it("returns null when no stable release exists yet (404)", async () => {
    const mockFetch = vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 404 }));
    await expect(fetchLatestStableRelease(undefined, mockFetch)).resolves.toEqual({
      latestVersion: null,
      releaseUrl: null,
    });
  });

  it("ignores draft or prerelease responses and rejects non-OK HTTP statuses", async () => {
    const prereleaseFetch = vi
      .fn<typeof fetch>()
      .mockResolvedValue(new Response(JSON.stringify({ tag_name: "v0.2.0-rc.1", prerelease: true }), { status: 200 }));
    await expect(fetchLatestStableRelease(undefined, prereleaseFetch)).resolves.toEqual({
      latestVersion: null,
      releaseUrl: null,
    });

    const failingFetch = vi.fn<typeof fetch>().mockResolvedValue(new Response(null, { status: 403 }));
    await expect(fetchLatestStableRelease(undefined, failingFetch)).rejects.toThrow("HTTP 403");
  });
});
