import { useQuery } from "@tanstack/react-query";
import { api, call } from "./client";
import type { components } from "./schema";

export type ConfigView = components["schemas"]["ConfigView"];
export type ConfigFile = components["schemas"]["ConfigFile"];
export type ConfigKey = components["schemas"]["ConfigKey"];
export type ConfigKeyPath = components["schemas"]["ConfigKeyPath"];
export type ConfigProblem = components["schemas"]["ConfigProblem"];
export type ConfigSaved = components["schemas"]["ConfigSaved"];
export type RestartChange = components["schemas"]["RestartChange"];
export type SecretState = components["schemas"]["SecretState"];
export type SecretShown = components["schemas"]["SecretShown"];

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
    queryFn: () => call(api.GET("/api/config")),
    refetchOnWindowFocus: false,
  });
}

/**
 * Saves the whole config, based on the file whose hash is `hash`: the service validates it, writes it and reloads. A file
 * changed since is refused (`ConfigChanged`); a config that does not validate is refused with every problem
 * (`ConfigRejected`). The secrets in `config` are ignored: `secrets` names those to change, a text writing it and `null`
 * removing it, and every other keeps what the file holds. The answer names the settings saved with a value the service
 * takes only when it restarts.
 */
export function saveConfig(
  hash: string,
  config: ConfigFile,
  secrets: Readonly<Record<string, string | null>>,
): Promise<ConfigSaved> {
  return call(api.PUT("/api/config", { body: { hash, config, secrets } }));
}

/** The value of the secret at the key path `path` as the service would use it: the environment variable's, else the file's. */
export function getSecret(path: string): Promise<SecretShown> {
  return call(api.GET("/api/config/secrets/{path}", { params: { path: { path } } }));
}
