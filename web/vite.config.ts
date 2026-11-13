import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { fileURLToPath } from "node:url";
import { defineConfig } from "vitest/config";

// The resident service the dev server forwards /api to. It listens on 127.0.0.1 at server.port, 18888 by default.
const servicePort = process.env.TACIT_SERVICE_PORT ?? "18888";

export default defineConfig({
  base: "/ui/",
  plugins: [react(), tailwindcss()],
  resolve: {
    // The shadcn/ui command line writes its imports with the "@/" alias, as components.json names it; the alias keeps them resolving.
    alias: {
      "@": fileURLToPath(new URL("./src", import.meta.url)),
    },
  },
  server: {
    proxy: {
      "/api": {
        target: `http://127.0.0.1:${servicePort}`,
        // The service refuses Host names other than loopback names, so the Host header is the target's.
        changeOrigin: true,
        // The service refuses any request that carries an Origin header which is not its own, and the dev page's Origin is not.
        configure: (proxy) => {
          proxy.on("proxyReq", (proxyReq) => {
            proxyReq.removeHeader("origin");
          });
        },
      },
    },
  },
  test: {
    environment: "node",
    include: ["src/**/*.test.ts"],
  },
});
