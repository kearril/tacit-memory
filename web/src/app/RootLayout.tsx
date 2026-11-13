import { useQuery, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef } from "react";
import { Navigate, Outlet, useLocation, useNavigate } from "react-router";
import { onUnauthorized } from "../api/client";
import { getSession, SESSION_QUERY_KEY } from "../api/session";
import { Toaster } from "../components/ui/toaster";
import { dictionaryFor, errorTextOf } from "../i18n/messages";
import { destinationAfterLogin, targetOf } from "./return-target";
import { SessionContext } from "./session-context";

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
      if (current.pathname === "/login") {
        return;
      }
      navigate("/login", { replace: true, state: { from: targetOf(current) } });
    });
  }, [navigate, queryClient]);

  useEffect(() => {
    if (session.data !== undefined) {
      document.documentElement.lang = session.data.language;
    }
  }, [session.data]);

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
  const onLoginPage = location.pathname === "/login";

  if (!allowed && !onLoginPage) {
    return <Navigate to="/login" replace state={{ from: targetOf(location) }} />;
  }
  if (allowed && onLoginPage) {
    return <Navigate to={destinationAfterLogin(location.state)} replace />;
  }

  return (
    <SessionContext.Provider value={status}>
      <Toaster closeLabel={dictionary.dismiss} viewportLabel={dictionary.notifications}>
        <Outlet />
      </Toaster>
    </SessionContext.Provider>
  );
}
