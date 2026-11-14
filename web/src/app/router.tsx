import { createBrowserRouter, Navigate } from "react-router";
import { DashboardPage } from "../pages/dashboard/DashboardPage";
import { LoginPage } from "../pages/login/LoginPage";
import { PlaceholderPage } from "../pages/PlaceholderPage";
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
            { path: "logs", element: <PlaceholderPage section="logs" /> },
            { path: "memory", element: <PlaceholderPage section="memory" /> },
          ],
        },
        { path: "*", element: <Navigate to="/dashboard" replace /> },
      ],
    },
  ],
  { basename: UI_BASE },
);
