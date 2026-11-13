import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router";
import { logout, SESSION_QUERY_KEY } from "../api/session";
import { Button } from "../components/ui/button";
import { useDictionary, useSessionStatus } from "./session-context";

const navItems = [
  { to: "/dashboard", key: "dashboard" },
  { to: "/settings", key: "settings" },
  { to: "/prompts", key: "prompts" },
  { to: "/logs", key: "logs" },
  { to: "/memory", key: "memory" },
] as const;

/**
 * The frame of every page once the session allows it: the five navigation items, which collapse into a disclosure on a
 * phone-width screen, and the log-out control on a remote connection.
 */
export function AppShell() {
  const dictionary = useDictionary();
  const session = useSessionStatus();
  const queryClient = useQueryClient();
  const navigate = useNavigate();
  const [menuOpen, setMenuOpen] = useState(false);

  const leave = useMutation({
    mutationFn: logout,
    onSettled: () => {
      queryClient.setQueryData(SESSION_QUERY_KEY, { ...session, loggedIn: false });
      navigate("/login", { replace: true });
    },
  });

  return (
    <div className="min-h-dvh bg-canvas text-ink">
      <header className="border-b-(length:--tk-border-thin) border-line bg-surface">
        <div className="mx-auto flex max-w-container-content items-center justify-between gap-md p-md">
          <span className="text-lg font-semibold">{dictionary.appName}</span>
          <div className="flex items-center gap-sm">
            {session.local ? null : (
              <Button variant="ghost" disabled={leave.isPending} onClick={() => leave.mutate()}>
                {dictionary.logout}
              </Button>
            )}
            <Button
              variant="outline"
              className="md:hidden"
              aria-expanded={menuOpen}
              aria-controls="main-navigation"
              onClick={() => setMenuOpen((open) => !open)}
            >
              {menuOpen ? dictionary.menuClose : dictionary.menuOpen}
            </Button>
          </div>
        </div>
        <nav
          id="main-navigation"
          aria-label={dictionary.mainNav}
          className={menuOpen ? "block border-t-(length:--tk-border-thin) border-line md:block" : "hidden md:block"}
        >
          <ul className="mx-auto flex max-w-container-content flex-col gap-xs p-sm md:flex-row md:flex-wrap">
            {navItems.map((item) => (
              <li key={item.to}>
                <NavLink
                  to={item.to}
                  onClick={() => setMenuOpen(false)}
                  className={({ isActive }) =>
                    `inline-flex h-control items-center rounded-md px-md text-sm transition-colors ${
                      isActive ? "bg-accent text-on-accent" : "text-ink hover:bg-surface-raised"
                    }`
                  }
                >
                  {dictionary.nav[item.key]}
                </NavLink>
              </li>
            ))}
          </ul>
        </nav>
      </header>
      <main className="mx-auto max-w-container-content p-md">
        <Outlet />
      </main>
    </div>
  );
}
