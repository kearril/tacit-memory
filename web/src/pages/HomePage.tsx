import { useQuery } from "@tanstack/react-query";
import { useEffect } from "react";
import { ApiError } from "../api/client";
import { getSession } from "../api/session";
import { dictionaryFor } from "../i18n/messages";

/** The only page of this stage: one sentence in the memory language, and the service version it got from the session. */
export function HomePage() {
  const session = useQuery({ queryKey: ["session"], queryFn: getSession });
  const language = session.data?.language;

  useEffect(() => {
    if (language !== undefined) {
      document.documentElement.lang = language;
    }
  }, [language]);

  const dictionary = dictionaryFor(language);

  if (session.isError) {
    const message = session.error instanceof ApiError ? session.error.message : dictionary.unreachable;
    return (
      <main className="flex min-h-dvh items-center justify-center p-md">
        <p role="alert" className="text-base text-danger">
          {message}
        </p>
      </main>
    );
  }

  if (session.data === undefined) {
    return <main className="min-h-dvh p-md" aria-busy="true" />;
  }

  return (
    <main className="flex min-h-dvh flex-col items-center justify-center gap-sm p-md">
      <p className="text-lg text-ink">{dictionary.connected}</p>
      <p className="text-sm text-ink-muted">
        {dictionary.serviceVersion}
        <span className="font-mono">{session.data.version}</span>
      </p>
    </main>
  );
}
