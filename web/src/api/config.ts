import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "./client";
import type { components } from "./schema";

export type ConfigView = components["schemas"]["ConfigView"];
export type ConfigFile = components["schemas"]["ConfigFile"];
export type ConfigKey = components["schemas"]["ConfigKey"];
export type ConfigProblem = components["schemas"]["ConfigProblem"];
export type ConfigSaved = components["schemas"]["ConfigSaved"];
export type RestartChange = components["schemas"]["RestartChange"];

/** The query key of the settings. */
export const CONFIG_QUERY_KEY = ["config"] as const;

/**
 * The settings as `config.yaml` holds them, as the service runs them, and their defaults, with each key's limits and the
 * file's hash. Fetched once per visit of the settings page and after a save; not on window focus, as a fetch while
 * the user edits must not move the page under them.
 */
export function useConfig() {
  return useQuery({
    queryKey: CONFIG_QUERY_KEY,
    queryFn: () => apiFetch<ConfigView>("/api/config"),
    refetchOnWindowFocus: false,
  });
}

/**
 * Saves the whole config, based on the file whose hash is `hash`: the service validates it, writes it and reloads. A file
 * changed since is refused (`ConfigChanged`); a config that does not validate is refused with every problem
 * (`ConfigRejected`). Secrets left `null` keep the values the file holds.
 */
export function saveConfig(hash: string, config: ConfigFile): Promise<ConfigSaved> {
  return apiFetch<ConfigSaved>("/api/config", { method: "PUT", body: JSON.stringify({ hash, config }) });
}
