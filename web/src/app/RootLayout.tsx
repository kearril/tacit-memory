import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import { Navigate, Outlet, useLocation, useNavigate } from "react-router";
import { onUnauthorized } from "../api/client";
import { getSession, SESSION_QUERY_KEY } from "../api/session";
import { Toaster } from "../components/ui/toaster";
import { TooltipProvider } from "../components/ui/tooltip";
import { dictionaryFor, errorTextOf } from "../i18n/messages";
import { pageTitleOf } from "./page-title";
import { destinationAfterLogin, targetOf } from "./return-target";
import { SessionContext } from "./session-context";
import { LOGIN_PATH } from "../lib/routes";

/**
 * The gate every page passes. It asks the service for the session first; a connection that is neither local nor logged
 * in goes to the login page, remembering where it was going, and a logged-in or local connection that opens the login
 * page goes on to its destination. A call that the service answers 401 (the session is gone) sends the user to the login
 * page in the same way.
 */
export function RootLayout() {
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const session = useQuery({ queryKey: SESSION_QUERY_KEY, queryFn: getSession });

  // The location is read by the unauthorized listener, which is not a render; the ref keeps the latest one.
  const locationRef = useRef(location);
  useEffect(() => {
    locationRef.current = location;
  });

  useEffect(() => {
    return onUnauthorized(() => {
      void queryClient.invalidateQueries({ queryKey: SESSION_QUERY_KEY });
      const current = locationRef.current;
      if (current.pathname === LOGIN_PATH) {
        return;
      }
      navigate(LOGIN_PATH, { replace: true, state: { from: targetOf(current) } });
    });
  }, [navigate, queryClient]);

  useEffect(() => {
    if (session.data !== undefined) {
      document.documentElement.lang = session.data.language;
    }
  }, [session.data]);

  useEffect(() => {
    document.title =
      session.data === undefined
        ? dictionaryFor(undefined).appName
        : pageTitleOf(location.pathname, dictionaryFor(session.data.language));
  }, [location.pathname, session.data]);

  if (session.isError) {
    return (
      <main className="flex min-h-dvh items-center justify-center p-md">
        <p role="alert" className="text-base text-danger">
          {errorTextOf(session.error, dictionaryFor(undefined))}
        </p>
      </main>
    );
  }

  if (session.data === undefined) {
    return <main className="min-h-dvh p-md" aria-busy="true" />;
  }

  const status = session.data;
  const dictionary = dictionaryFor(status.language);
  const allowed = status.local || status.loggedIn;
  const onLoginPage = location.pathname === LOGIN_PATH;

  if (!allowed && !onLoginPage) {
    return <Navigate to={LOGIN_PATH} replace state={{ from: targetOf(location) }} />;
  }
  if (allowed && onLoginPage) {
    return <Navigate to={destinationAfterLogin(location.state)} replace />;
  }

  return (
    <SessionContext.Provider value={status}>
      <TooltipProvider>
        <Toaster closeLabel={dictionary.dismiss} viewportLabel={dictionary.notifications}>
          <Outlet />
        </Toaster>
      </TooltipProvider>
    </SessionContext.Provider>
  );
}
