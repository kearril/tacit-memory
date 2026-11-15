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
import { memoryPathOf } from "../pages/memory/views";
import { DEFAULT_PROMPT, promptPathOf } from "../pages/prompts/names";
import { PromptsPage } from "../pages/prompts/PromptsPage";
import { SettingsPage } from "../pages/settings/SettingsPage";
import { AppShell } from "./AppShell";
import { RootLayout } from "./RootLayout";
import { UI_BASE } from "./return-target";

export const router = createBrowserRouter(
  [
    {
      path: "/",
      Component: RootLayout,
      children: [
        { path: "login", Component: LoginPage },
        {
          Component: AppShell,
          children: [
            { index: true, element: <Navigate to="/dashboard" replace /> },
            { path: "dashboard", Component: DashboardPage },
            { path: "settings", Component: SettingsPage },
            { path: "prompts", element: <Navigate to={promptPathOf(DEFAULT_PROMPT)} replace /> },
            { path: "prompts/:name", Component: PromptsPage },
            { path: "logs", Component: LogsPage },
            { path: "logs/:date", Component: LogsPage },
            { path: "memory", element: <Navigate to={memoryPathOf("persona")} replace /> },
            { path: "memory/persona", Component: MemoryPersonaPage },
            { path: "memory/workstreams", Component: MemoryWorkstreamsPage },
            { path: "memory/workstreams/:name", Component: MemoryWorkstreamsPage },
            { path: "memory/pages", Component: MemoryPagesPage },
            { path: "memory/pages/:workstream/:slug", Component: MemoryPagePage },
            { path: "memory/activity", Component: MemoryActivityPage },
            { path: "memory/search", Component: MemorySearchPage },
          ],
        },
        { path: "*", element: <Navigate to="/dashboard" replace /> },
      ],
    },
  ],
  { basename: UI_BASE },
);
