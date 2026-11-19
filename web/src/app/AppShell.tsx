import { useMutation, useQueryClient } from "@tanstack/react-query";
import {
  ChevronDown,
  FileText,
  LayoutDashboard,
  Library,
  LogOut,
  type LucideIcon,
  Menu,
  Monitor,
  MonitorSmartphone,
  PencilLine,
  ScrollText,
  Settings,
  TriangleAlert,
  X,
} from "lucide-react";
import { useState } from "react";
import { Link, NavLink, Outlet, useLocation, useNavigate } from "react-router";
import { usePrompts } from "../api/prompts";
import { logout, SESSION_QUERY_KEY } from "../api/session";
import { AppIcon } from "../components/app-icon";
import { Button } from "../components/ui/button";
import { Sheet, SheetClose, SheetContent, SheetTitle, SheetTrigger } from "../components/ui/sheet";
import { useSectionNav } from "../pages/settings/section-nav";
import { scrollBehavior } from "../lib/motion";
import { useDictionary, useSessionStatus } from "./session-context";
import {
  DASHBOARD_PATH,
  LOGIN_PATH,
  LOGS_PATH,
  MEMORY_PATH,
  MEMORY_VIEWS,
  memoryPathOf,
  PROMPT_NAMES,
  promptPathOf,
  PROMPTS_PATH,
  sectionIdOf,
  SECTIONS,
  SETTINGS_PATH,
  settingsPathOf,
} from "../lib/routes";

const navItems: readonly {
  to: string;
  key: "dashboard" | "settings" | "prompts" | "logs" | "memory";
  icon: LucideIcon;
}[] = [
  { to: DASHBOARD_PATH, key: "dashboard", icon: LayoutDashboard },
  { to: SETTINGS_PATH, key: "settings", icon: Settings },
  { to: PROMPTS_PATH, key: "prompts", icon: FileText },
  { to: LOGS_PATH, key: "logs", icon: ScrollText },
  { to: MEMORY_PATH, key: "memory", icon: Library },
];

/**
 * The frame of every page once the session allows it: a sidebar on the left with the brand, the five navigation items
 * (on the settings page, the list of its sections under its name; on the prompts page, the list of its documents; on the
 * memory page, the list of its views) and who
 * the connection is, and the page centred in the
 * space to its right. On a narrow screen the sidebar becomes a drawer
 * that a menu button in the page header opens.
 */
export function AppShell() {
  const dictionary = useDictionary();
  const [menuOpen, setMenuOpen] = useState(false);

  return (
    <div className="min-h-dvh bg-canvas text-ink lg:grid lg:grid-cols-[var(--tk-size-sidebar)_minmax(0,1fr)]">
      <a
        href="#main-content"
        className="sr-only focus:not-sr-only focus:fixed focus:top-sm focus:left-sm focus:z-(--tk-layer-toast) focus:rounded-md focus:bg-accent focus:px-md focus:py-sm focus:text-sm focus:font-medium focus:text-on-accent focus:shadow-raised focus:outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent focus-visible:ring-offset-(length:--tk-border-thick) focus-visible:ring-offset-surface"
      >
        {dictionary.skipToContent}
      </a>
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

      <main
        id="main-content"
        tabIndex={-1}
        className="mx-auto w-full max-w-page px-md py-lg outline-none md:px-xl md:py-xl"
      >
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
                  `flex min-h-control items-center gap-sm rounded-md px-md py-xs text-sm font-medium transition-colors outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
                    isActive ? "bg-accent-soft text-accent" : "text-ink-muted hover:bg-surface-raised hover:text-ink"
                  }`
                }
              >
                <item.icon aria-hidden className="size-(--tk-font-size-lg) shrink-0" />
                {dictionary.nav[item.key]}
              </NavLink>
              {item.key === "settings" ? <SettingsSections onNavigate={onNavigate} /> : null}
              {item.key === "prompts" ? <PromptDocuments onNavigate={onNavigate} /> : null}
              {item.key === "memory" ? <MemoryViews onNavigate={onNavigate} /> : null}
            </li>
          ))}
        </ul>
      </nav>
      <Connection />
    </div>
  );
}

/** On the memory page, the list of its five views under its name in the sidebar: each opens its view, the open one is marked. */
function MemoryViews({ onNavigate }: { readonly onNavigate: () => void }) {
  const dictionary = useDictionary();
  const { pathname } = useLocation();
  if (!pathname.startsWith(MEMORY_PATH)) {
    return null;
  }

  return (
    <ul
      aria-label={dictionary.memory.viewsNav}
      className="mt-xs ml-lg grid gap-xs border-l-(length:--tk-border-thin) border-line"
    >
      {MEMORY_VIEWS.map((view) => {
        const path = memoryPathOf(view);
        const active = pathname === path || pathname.startsWith(`${path}/`);
        return (
          <li key={view}>
            <Link
              to={path}
              aria-current={active ? "page" : undefined}
              onClick={onNavigate}
              className={`-ml-(--tk-border-thin) flex items-center gap-sm border-l-(length:--tk-border-thick) py-xs pr-sm pl-md text-sm transition-colors outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
                active ? "border-accent font-medium text-accent" : "border-transparent text-ink-muted hover:text-ink"
              }`}
            >
              {dictionary.memory.views[view]}
            </Link>
          </li>
        );
      })}
    </ul>
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
  if (pathname !== SETTINGS_PATH) {
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
        <ul
          id="settings-sections"
          aria-label={text.sectionsNav}
          className="mt-xs ml-lg grid gap-xs border-l-(length:--tk-border-thin) border-line"
        >
          {SECTIONS.map((section) => {
            const active = nav.active === section;
            const problems = nav.problems[section] ?? 0;
            return (
              <li key={section}>
                <Link
                  to={settingsPathOf(section)}
                  aria-current={active ? "location" : undefined}
                  onClick={() => {
                    document
                      .getElementById(sectionIdOf(section))
                      ?.scrollIntoView({ behavior: scrollBehavior(), block: "start" });
                    onNavigate();
                  }}
                  className={`-ml-(--tk-border-thin) flex items-center justify-between gap-sm border-l-(length:--tk-border-thick) py-xs pr-sm pl-md text-sm transition-colors outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
                    active
                      ? "border-accent font-medium text-accent"
                      : "border-transparent text-ink-muted hover:text-ink"
                  }`}
                >
                  {text.sections[section]}
                  {problems > 0 ? (
                    <span
                      aria-label={text.problemsIn(problems)}
                      className="rounded-full bg-danger-soft px-sm text-xs text-danger"
                    >
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

/**
 * On the prompts page, the list of its four documents under its name in the sidebar: each opens its document, the open one
 * is marked, and a pencil marks the documents the user has a file for.
 */
function PromptDocuments({ onNavigate }: { readonly onNavigate: () => void }) {
  const dictionary = useDictionary();
  const text = dictionary.prompts;
  const { pathname } = useLocation();
  const prompts = usePrompts();
  if (!pathname.startsWith(PROMPTS_PATH)) {
    return null;
  }

  return (
    <ul
      aria-label={text.documentsNav}
      className="mt-xs ml-lg grid gap-xs border-l-(length:--tk-border-thin) border-line"
    >
      {PROMPT_NAMES.map((name) => {
        const path = promptPathOf(name);
        const active = pathname === path;
        const document = prompts.data?.documents.find((candidate) => candidate.name === name);
        const customized = document?.builtIn === false;
        const invalid = document?.problem != null;
        return (
          <li key={name}>
            <Link
              to={path}
              aria-current={active ? "page" : undefined}
              onClick={onNavigate}
              className={`-ml-(--tk-border-thin) flex items-center justify-between gap-sm border-l-(length:--tk-border-thick) py-xs pr-sm pl-md text-sm transition-colors outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
                active ? "border-accent font-medium text-accent" : "border-transparent text-ink-muted hover:text-ink"
              }`}
            >
              {text.documents[name].title}
              {invalid ? (
                <TriangleAlert
                  role="img"
                  aria-label={text.invalid}
                  className="size-(--tk-font-size-sm) shrink-0 text-danger"
                >
                  <title>{text.invalid}</title>
                </TriangleAlert>
              ) : customized ? (
                <PencilLine role="img" aria-label={text.customized} className="size-(--tk-font-size-sm) shrink-0">
                  <title>{text.customized}</title>
                </PencilLine>
              ) : null}
            </Link>
          </li>
        );
      })}
    </ul>
  );
}

function Brand() {
  const dictionary = useDictionary();
  return (
    <span className="flex items-center gap-sm text-base font-semibold">
      <AppIcon />
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
      navigate(LOGIN_PATH, { replace: true });
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
