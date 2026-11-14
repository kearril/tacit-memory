import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  Brain,
  ChevronDown,
  FileText,
  LayoutDashboard,
  Library,
  LogOut,
  type LucideIcon,
  Menu,
  Monitor,
  MonitorSmartphone,
  ScrollText,
  Settings,
  X,
} from "lucide-react";
import { useState } from "react";
import { Link, NavLink, Outlet, useLocation, useNavigate } from "react-router";
import { logout, SESSION_QUERY_KEY } from "../api/session";
import { Button } from "../components/ui/button";
import { Sheet, SheetClose, SheetContent, SheetTitle, SheetTrigger } from "../components/ui/sheet";
import { SECTIONS, sectionIdOf } from "../pages/settings/form";
import { useSectionNav } from "../pages/settings/section-nav";
import { useDictionary, useSessionStatus } from "./session-context";

const navItems: readonly { to: string; key: "dashboard" | "settings" | "prompts" | "logs" | "memory"; icon: LucideIcon }[] = [
  { to: "/dashboard", key: "dashboard", icon: LayoutDashboard },
  { to: "/settings", key: "settings", icon: Settings },
  { to: "/prompts", key: "prompts", icon: FileText },
  { to: "/logs", key: "logs", icon: ScrollText },
  { to: "/memory", key: "memory", icon: Library },
];

/**
 * The frame of every page once the session allows it: a sidebar on the left with the brand, the five navigation items
 * (on the settings page, the list of its sections under its name) and who the connection is, and the page centred in the
 * space to its right. On a narrow screen the sidebar becomes a drawer
 * that a menu button in the page header opens.
 */
export function AppShell() {
  const dictionary = useDictionary();
  const [menuOpen, setMenuOpen] = useState(false);

  return (
    <div className="min-h-dvh bg-canvas text-ink lg:grid lg:grid-cols-[var(--tk-size-sidebar)_minmax(0,1fr)]">
      <aside className="sticky top-0 hidden h-dvh border-r-(length:--tk-border-thin) border-line bg-surface lg:flex lg:flex-col">
        <SidebarBody onNavigate={() => undefined} />
      </aside>

      <header className="sticky top-0 z-(--tk-layer-nav) flex items-center justify-between gap-md border-b-(length:--tk-border-thin) border-line bg-surface px-md py-sm lg:hidden">
        <Brand />
        <Sheet open={menuOpen} onOpenChange={setMenuOpen}>
          <SheetTrigger asChild>
            <Button variant="ghost" size="icon" aria-label={dictionary.menuOpen}>
              <Menu aria-hidden />
            </Button>
          </SheetTrigger>
          <SheetContent aria-describedby={undefined}>
            <SheetTitle>{dictionary.mainNav}</SheetTitle>
            <div className="absolute top-md right-sm">
              <SheetClose asChild>
                <Button variant="ghost" size="icon" aria-label={dictionary.menuClose}>
                  <X aria-hidden />
                </Button>
              </SheetClose>
            </div>
            <SidebarBody onNavigate={() => setMenuOpen(false)} />
          </SheetContent>
        </Sheet>
      </header>

      <main className="mx-auto w-full max-w-page px-md py-lg md:px-xl md:py-xl">
        <Outlet />
      </main>
    </div>
  );
}

/** The content of the sidebar, shared by the fixed sidebar and the drawer. */
function SidebarBody({ onNavigate }: { readonly onNavigate: () => void }) {
  const dictionary = useDictionary();
  return (
    <div className="flex h-full flex-col gap-lg p-md">
      <div className="px-sm pt-sm">
        <Brand />
      </div>
      <nav aria-label={dictionary.mainNav} className="flex-1">
        <ul className="grid gap-xs">
          {navItems.map((item) => (
            <li key={item.to} className="relative">
              <NavLink
                to={item.to}
                onClick={onNavigate}
                className={({ isActive }) =>
                  `flex h-control items-center gap-sm rounded-md px-md text-sm font-medium transition-colors outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
                    isActive ? "bg-accent-soft text-accent" : "text-ink-muted hover:bg-surface-raised hover:text-ink"
                  }`
                }
              >
                <item.icon aria-hidden className="size-(--tk-font-size-lg) shrink-0" />
                {dictionary.nav[item.key]}
              </NavLink>
              {item.key === "settings" ? <SettingsSections onNavigate={onNavigate} /> : null}
            </li>
          ))}
        </ul>
      </nav>
      <Connection />
    </div>
  );
}

const SECTIONS_OPEN_KEY = "tacit.settingsSectionsOpen";

/**
 * On the settings page, the list of its sections under its name in the sidebar: each scrolls to its section and shows how
 * many problems the section has, and the one being read is marked. A button on the page's row folds the list away; the
 * browser remembers whether it is folded.
 */
function SettingsSections({ onNavigate }: { readonly onNavigate: () => void }) {
  const dictionary = useDictionary();
  const text = dictionary.settings;
  const { pathname } = useLocation();
  const nav = useSectionNav();
  const [open, setOpen] = useState(() => {
    try {
      return window.localStorage.getItem(SECTIONS_OPEN_KEY) !== "false";
    } catch {
      return true;
    }
  });
  if (pathname !== "/settings") {
    return null;
  }

  const toggle = () => {
    setOpen(!open);
    try {
      window.localStorage.setItem(SECTIONS_OPEN_KEY, String(!open));
    } catch {
      // Without storage the list just opens again on the next visit.
    }
  };

  return (
    <>
      <Button
        variant="ghost"
        size="icon"
        aria-expanded={open}
        aria-controls="settings-sections"
        aria-label={text.sectionsNav}
        title={text.sectionsNav}
        className="absolute top-0 right-0 text-accent"
        onClick={toggle}
      >
        <ChevronDown aria-hidden className={`transition-transform ${open ? "" : "-rotate-90"}`} />
      </Button>
      {open ? (
        <ul id="settings-sections" aria-label={text.sectionsNav} className="mt-xs ml-lg grid gap-xs border-l-(length:--tk-border-thin) border-line">
          {SECTIONS.map((section) => {
            const active = nav.active === section;
            const problems = nav.problems[section] ?? 0;
            return (
              <li key={section}>
                <Link
                  to={`/settings#${sectionIdOf(section)}`}
                  aria-current={active ? "location" : undefined}
                  onClick={() => {
                    document.getElementById(sectionIdOf(section))?.scrollIntoView({ behavior: "smooth", block: "start" });
                    onNavigate();
                  }}
                  className={`-ml-(--tk-border-thin) flex items-center justify-between gap-sm border-l-(length:--tk-border-thick) py-xs pr-sm pl-md text-sm transition-colors outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
                    active ? "border-accent font-medium text-accent" : "border-transparent text-ink-muted hover:text-ink"
                  }`}
                >
                  {text.sections[section]}
                  {problems > 0 ? (
                    <span aria-label={text.problemsIn(problems)} className="rounded-full bg-danger-soft px-sm text-xs text-danger">
                      {problems}
                    </span>
                  ) : null}
                </Link>
              </li>
            );
          })}
        </ul>
      ) : null}
    </>
  );
}

function Brand() {
  const dictionary = useDictionary();
  return (
    <span className="flex items-center gap-sm text-base font-semibold">
      <span className="grid size-(--tk-size-control) place-items-center rounded-md bg-accent text-on-accent shadow-pressed">
        <Brain aria-hidden className="size-(--tk-font-size-lg)" />
      </span>
      {dictionary.appName}
    </span>
  );
}

/** Who the connection is: this computer, which needs no login, or another device that has logged in and can log out. */
function Connection() {
  const dictionary = useDictionary();
  const session = useSessionStatus();
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  const leave = useMutation({
    mutationFn: logout,
    onSettled: () => {
      queryClient.setQueryData(SESSION_QUERY_KEY, { ...session, loggedIn: false });
      navigate("/login", { replace: true });
    },
  });

  const Icon = session.local ? Monitor : MonitorSmartphone;
  return (
    <div className="flex items-center gap-sm rounded-md bg-surface-raised p-sm">
      <span className="grid size-(--tk-size-control) shrink-0 place-items-center rounded-sm bg-surface text-ink-muted">
        <Icon aria-hidden className="size-(--tk-font-size-base)" />
      </span>
      <span className="grid min-w-0 flex-1 leading-(--tk-line-height-tight)">
        <span className="truncate text-sm font-medium">
          {session.local ? dictionary.connection.local : dictionary.connection.remote}
        </span>
        <span className="truncate text-xs text-ink-muted">
          {session.local ? dictionary.connection.localNote : dictionary.connection.remoteNote}
        </span>
      </span>
      {session.local ? null : (
        <Button
          variant="ghost"
          size="icon"
          aria-label={dictionary.logout}
          title={dictionary.logout}
          disabled={leave.isPending}
          onClick={() => leave.mutate()}
        >
          <LogOut aria-hidden />
        </Button>
      )}
    </div>
  );
}
