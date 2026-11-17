import { useQuery } from "@tanstack/react-query";
import { api, call } from "./client";
import type { Affects } from "./effects";
import { STATUS_QUERY_KEY } from "./status";
import type { components } from "./schema";

export type BindingList = components["schemas"]["BindingList"];
export type McpServer = components["schemas"]["McpServer"];
export type BindingCheck = components["schemas"]["BindingCheck"];
export type BindingDone = components["schemas"]["BindingDone"];
export type ConnectRequest = components["schemas"]["ConnectRequest"];

/** The query key of the bindings. */
export const BINDINGS_QUERY_KEY = ["bindings"] as const;

/** The bindings and what is wrong with each, as the status report's bindings column has them, with the MCP server to register. */
export function useBindings() {
  return useQuery({
    queryKey: BINDINGS_QUERY_KEY,
    queryFn: () => call(api.GET("/api/bindings")),
  });
}

/** Binds a client by the rules of `tacit connect`; a refusal carries the reason the command gives. */
export function connectClient(request: ConnectRequest): Promise<BindingDone> {
  return call(api.POST("/api/bindings", { body: request }));
}

/** Unbinds a client by the rules of `tacit disconnect`. */
export function disconnectClient(client: string): Promise<BindingDone> {
  return call(api.DELETE("/api/bindings/{client}", { params: { path: { client } } }));
}

/** A bind or an unbind changes the binding list and the binding problems the status reports. */
export const BIND_AFFECTS: Affects = [BINDINGS_QUERY_KEY, STATUS_QUERY_KEY];
