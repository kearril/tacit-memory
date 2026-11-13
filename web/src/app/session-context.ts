import { createContext, useContext } from "react";
import type { SessionStatus } from "../api/session";
import { type Dictionary, dictionaryFor } from "../i18n/messages";

/**
 * The session status, provided by the root layout once the service has answered. Pages below it read it here, so they
 * never see a state in which the session is still unknown.
 */
export const SessionContext = createContext<SessionStatus | undefined>(undefined);

export function useSessionStatus(): SessionStatus {
  const session = useContext(SessionContext);
  if (session === undefined) {
    throw new Error("The session status is read only below the root layout, after the service has answered.");
  }
  return session;
}

/** The text of the interface, in the memory language the session status names. */
export function useDictionary(): Dictionary {
  return dictionaryFor(useSessionStatus().language);
}
