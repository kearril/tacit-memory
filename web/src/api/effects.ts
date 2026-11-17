import type { QueryClient, QueryKey } from "@tanstack/react-query";

/**
 * The queries a command changes the answer of: the key prefixes to invalidate once it ends, or `"everything"` for a
 * command that may change any of them. Each command declares its own next to its function.
 */
export type Affects = readonly QueryKey[] | "everything";

/** Invalidates the queries `affects` names; the mounted ones refetch, the others are fetched again when next read. */
export async function afterCommand(client: QueryClient, affects: Affects): Promise<void> {
  if (affects === "everything") {
    await client.invalidateQueries();
    return;
  }
  await Promise.all(affects.map((queryKey) => client.invalidateQueries({ queryKey })));
}
