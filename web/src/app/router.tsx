import { createBrowserRouter, Navigate } from "react-router";
import { DashboardPage } from "../pages/dashboard/DashboardPage";
import { LoginPage } from "../pages/login/LoginPage";
import { LogsPage } from "../pages/logs/LogsPage";
import { MemoryActivityPage } from "../pages/memory/MemoryActivityPage";
import { MemoryPagePage } from "../pages/memory/MemoryPagePage";
import { MemoryPagesPage } from "../pages/memory/MemoryPagesPage";
import { MemoryPersonaPage } from "../pages/memory/MemoryPersonaPage";
import { MemorySearchPage } from "../pages/memory/MemorySearchPage";
import { MemoryWorkstreamsPage } from "../pages/memory/MemoryWorkstreamsPage";
import { PromptsPage } from "../pages/prompts/PromptsPage";
import { SettingsPage } from "../pages/settings/SettingsPage";
import { AppShell } from "./AppShell";
import { RootLayout } from "./RootLayout";
import { UI_BASE } from "./return-target";
import {
  DASHBOARD_PATH,
  DEFAULT_PROMPT,
  LOG_ROUTE,
  LOGIN_PATH,
  LOGS_PATH,
  MEMORY_PATH,
  memoryPathOf,
  PAGE_ROUTE,
  PROMPT_ROUTE,
  promptPathOf,
  PROMPTS_PATH,
  SETTINGS_PATH,
  WORKSTREAM_ROUTE,
} from "../lib/routes";

export const router = createBrowserRouter(
  [
    {
      path: "/",
      Component: RootLayout,
      children: [
        { path: LOGIN_PATH, Component: LoginPage },
        {
          Component: AppShell,
          children: [
            { index: true, element: <Navigate to={DASHBOARD_PATH} replace /> },
            { path: DASHBOARD_PATH, Component: DashboardPage },
            { path: SETTINGS_PATH, Component: SettingsPage },
            { path: PROMPTS_PATH, element: <Navigate to={promptPathOf(DEFAULT_PROMPT)} replace /> },
            { path: PROMPT_ROUTE, Component: PromptsPage },
            { path: LOGS_PATH, Component: LogsPage },
            { path: LOG_ROUTE, Component: LogsPage },
            { path: MEMORY_PATH, element: <Navigate to={memoryPathOf("persona")} replace /> },
            { path: memoryPathOf("persona"), Component: MemoryPersonaPage },
            { path: memoryPathOf("workstreams"), Component: MemoryWorkstreamsPage },
            { path: WORKSTREAM_ROUTE, Component: MemoryWorkstreamsPage },
            { path: memoryPathOf("pages"), Component: MemoryPagesPage },
            { path: PAGE_ROUTE, Component: MemoryPagePage },
            { path: memoryPathOf("activity"), Component: MemoryActivityPage },
            { path: memoryPathOf("search"), Component: MemorySearchPage },
          ],
        },
        { path: "*", element: <Navigate to={DASHBOARD_PATH} replace /> },
      ],
    },
  ],
  { basename: UI_BASE },
);
