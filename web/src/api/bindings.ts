import { useQuery } from "@tanstack/react-query";
import { apiFetch } from "./client";
import type { components } from "./schema";

export type BindingsStatus = components["schemas"]["BindingsStatus"];
export type BindingCheck = components["schemas"]["BindingCheck"];
export type BindingDone = components["schemas"]["BindingDone"];
export type ConnectRequest = components["schemas"]["ConnectRequest"];

/** The query key of the bindings. */
export const BINDINGS_QUERY_KEY = ["bindings"] as const;

/** The bindings and what is wrong with each, as the status report's bindings column has them. */
export function useBindings() {
  return useQuery({
    queryKey: BINDINGS_QUERY_KEY,
    queryFn: () => apiFetch<BindingsStatus>("/api/bindings"),
  });
}

/** Binds a client by the rules of `tacit connect`; a refusal carries the reason the command gives. */
export function connectClient(request: ConnectRequest): Promise<BindingDone> {
  return apiFetch<BindingDone>("/api/bindings", { method: "POST", body: JSON.stringify(request) });
}

/** Unbinds a client by the rules of `tacit disconnect`. */
export function disconnectClient(client: string): Promise<BindingDone> {
  return apiFetch<BindingDone>(`/api/bindings/${encodeURIComponent(client)}`, { method: "DELETE" });
}
