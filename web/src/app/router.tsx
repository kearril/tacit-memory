import { createBrowserRouter } from "react-router";
import { HomePage } from "../pages/HomePage";

// Routes are written from the /ui base, which is the Vite base without its trailing slash.
const basename = import.meta.env.BASE_URL.replace(/\/$/, "");

export const router = createBrowserRouter([{ path: "/", Component: HomePage }], { basename });
