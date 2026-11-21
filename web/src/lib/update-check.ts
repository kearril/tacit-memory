import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";

/** Repository and release pages on GitHub. */
export const REPOSITORY_URL = "https://github.com/kearril/tacit-memory";
export const RELEASES_URL = `${REPOSITORY_URL}/releases`;
const LATEST_RELEASE_API = "https://api.github.com/repos/kearril/tacit-memory/releases/latest";

/** How long a cached release check stays fresh before another automatic check runs: 5 hours. */
export const UPDATE_CHECK_INTERVAL_MS = 5 * 60 * 60 * 1000;

const STORAGE_KEY = "tacit.updateCheck";
export const UPDATE_CHECK_QUERY_KEY = ["update-check"] as const;

export interface ParsedSemver {
  readonly major: number;
  readonly minor: number;
  readonly patch: number;
  readonly prerelease: readonly string[];
}

export interface CachedRelease {
  readonly checkedAt: number;
  readonly latestVersion: string | null;
  readonly releaseUrl: string | null;
}

export interface AvailableUpdate {
  readonly version: string;
  readonly url: string;
}

const SEMVER_PATTERN = /^v?(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+.*)?$/;

/**
 * Parses a SemVer string with an optional leading `v` and optional `+build` metadata.
 * Returns `null` when the input is not a valid `major.minor.patch` version.
 */
export function parseSemver(raw: string): ParsedSemver | null {
  const match = SEMVER_PATTERN.exec(raw.trim());
  if (match === null) {
    return null;
  }
  const major = Number(match[1]);
  const minor = Number(match[2]);
  const patch = Number(match[3]);
  const prerelease = match[4] === undefined ? [] : match[4].split(".");
  if (prerelease.some((part) => part.length === 0)) {
    return null;
  }
  return { major, minor, patch, prerelease };
}

/** Compares two parsed SemVer values according to SemVer 2.0.0 precedence. */
export function compareSemver(a: ParsedSemver, b: ParsedSemver): number {
  if (a.major !== b.major) return a.major - b.major;
  if (a.minor !== b.minor) return a.minor - b.minor;
  if (a.patch !== b.patch) return a.patch - b.patch;

  const aPre = a.prerelease;
  const bPre = b.prerelease;
  if (aPre.length === 0 && bPre.length === 0) return 0;
  if (aPre.length === 0) return 1;
  if (bPre.length === 0) return -1;

  const limit = Math.min(aPre.length, bPre.length);
  for (let i = 0; i < limit; i++) {
    const left = aPre[i];
    const right = bPre[i];
    if (left === right) continue;
    const leftNumeric = /^\d+$/.test(left);
    const rightNumeric = /^\d+$/.test(right);
    if (leftNumeric && rightNumeric) {
      return Number(left) - Number(right);
    }
    if (leftNumeric) return -1;
    if (rightNumeric) return 1;
    return left < right ? -1 : 1;
  }
  return aPre.length - bPre.length;
}

/**
 * Whether `latestVersion` is a stable release strictly newer than `currentVersion`.
 * Development placeholder builds (`0.0.0`) and unparseable versions never prompt.
 */
export function isNewerStableVersion(currentVersion: string, latestVersion: string): boolean {
  const current = parseSemver(currentVersion);
  const latest = parseSemver(latestVersion);
  if (current === null || latest === null) {
    return false;
  }
  if (current.major === 0 && current.minor === 0 && current.patch === 0) {
    return false;
  }
  if (latest.prerelease.length > 0) {
    return false;
  }
  return compareSemver(latest, current) > 0;
}

/** Reads the cached release entry from browser storage, returning `null` if missing or malformed. */
export function readCachedRelease(storage?: Storage): CachedRelease | null {
  try {
    const target = storage ?? window.localStorage;
    const raw = target.getItem(STORAGE_KEY);
    if (raw === null) return null;
    const parsed: unknown = JSON.parse(raw);
    if (typeof parsed !== "object" || parsed === null) return null;
    const record = parsed as Record<string, unknown>;
    if (typeof record.checkedAt !== "number" || !Number.isFinite(record.checkedAt)) return null;
    const latestVersion = typeof record.latestVersion === "string" ? record.latestVersion : null;
    const releaseUrl = typeof record.releaseUrl === "string" ? record.releaseUrl : null;
    return { checkedAt: record.checkedAt, latestVersion, releaseUrl };
  } catch {
    return null;
  }
}

/** Persists a release check entry in browser storage; silently ignored when storage is unavailable. */
export function writeCachedRelease(entry: CachedRelease, storage?: Storage): void {
  try {
    const target = storage ?? window.localStorage;
    target.setItem(STORAGE_KEY, JSON.stringify(entry));
  } catch {
    // Storage may be disabled; in-memory query cache still keeps the result for this session.
  }
}

interface GitHubLatestRelease {
  readonly tag_name?: unknown;
  readonly html_url?: unknown;
  readonly draft?: unknown;
  readonly prerelease?: unknown;
}

/** Fetches the latest stable release from GitHub. A 404 (no stable release yet) resolves to `null`. */
export async function fetchLatestStableRelease(
  signal?: AbortSignal,
  fetchImpl: typeof fetch = fetch,
): Promise<{ readonly latestVersion: string | null; readonly releaseUrl: string | null }> {
  const response = await fetchImpl(LATEST_RELEASE_API, {
    headers: { Accept: "application/vnd.github+json" },
    signal,
  });
  if (response.status === 404) {
    return { latestVersion: null, releaseUrl: null };
  }
  if (!response.ok) {
    throw new Error(`GitHub releases API returned HTTP ${response.status}`);
  }
  const body = (await response.json()) as GitHubLatestRelease;
  if (body.draft === true || body.prerelease === true || typeof body.tag_name !== "string") {
    return { latestVersion: null, releaseUrl: null };
  }
  const normalized = body.tag_name.trim().replace(/^v/i, "");
  if (parseSemver(normalized) === null) {
    return { latestVersion: null, releaseUrl: null };
  }
  const url =
    typeof body.html_url === "string" && body.html_url.startsWith(`${REPOSITORY_URL}/`) ? body.html_url : RELEASES_URL;
  return { latestVersion: normalized, releaseUrl: url };
}

function resolveAvailable(
  currentVersion: string | null | undefined,
  cached: CachedRelease | null | undefined,
): AvailableUpdate | null {
  if (!currentVersion || !cached?.latestVersion) {
    return null;
  }
  if (!isNewerStableVersion(currentVersion, cached.latestVersion)) {
    return null;
  }
  return { version: cached.latestVersion, url: cached.releaseUrl ?? RELEASES_URL };
}

/**
 * Checks GitHub for a newer stable release at most once every 5 hours (cached in `localStorage`) and exposes a manual
 * `checkNow` trigger that bypasses the cache. Automatic checks fail silently so offline environments are never disturbed.
 */
export function useUpdateCheck(currentVersion: string | null | undefined) {
  const queryClient = useQueryClient();

  const query = useQuery<CachedRelease>({
    queryKey: UPDATE_CHECK_QUERY_KEY,
    queryFn: async ({ signal }) => {
      const cached = readCachedRelease();
      const now = Date.now();
      if (cached !== null && now - cached.checkedAt < UPDATE_CHECK_INTERVAL_MS) {
        return cached;
      }
      try {
        const fetched = await fetchLatestStableRelease(signal);
        const entry: CachedRelease = { checkedAt: now, ...fetched };
        writeCachedRelease(entry);
        return entry;
      } catch {
        return cached ?? { checkedAt: 0, latestVersion: null, releaseUrl: null };
      }
    },
    staleTime: UPDATE_CHECK_INTERVAL_MS,
    refetchInterval: UPDATE_CHECK_INTERVAL_MS,
    refetchOnWindowFocus: false,
    retry: false,
  });

  const manual = useMutation<AvailableUpdate | null>({
    mutationFn: async () => {
      const fetched = await fetchLatestStableRelease();
      const entry: CachedRelease = { checkedAt: Date.now(), ...fetched };
      writeCachedRelease(entry);
      queryClient.setQueryData(UPDATE_CHECK_QUERY_KEY, entry);
      return resolveAvailable(currentVersion, entry);
    },
  });

  return {
    available: resolveAvailable(currentVersion, query.data),
    checking: manual.isPending,
    checkNow: manual.mutateAsync,
  };
}
