import tailwindcss from "@tailwindcss/vite";
import react from "@vitejs/plugin-react";
import { readFileSync } from "node:fs";
import { fileURLToPath } from "node:url";
import license from "rollup-plugin-license";
import { defineConfig } from "vitest/config";
import { UI_BASE } from "./src/app/return-target.ts";

// The resident service the dev server forwards /api to. It listens on 127.0.0.1 at server.port, 18888 by default.
const servicePort = process.env.TACIT_SERVICE_PORT ?? "18888";

// Packages that reach the build through a CSS @import, which the license plugin does not see: the Inter font files, and
// the base styles Tailwind writes into the stylesheet.
const CSS_PACKAGES = ["@fontsource-variable/inter", "tailwindcss"];

function cssPackage(name: string) {
  const folder = fileURLToPath(new URL(`./node_modules/${name}/`, import.meta.url));
  const manifest = JSON.parse(readFileSync(`${folder}package.json`, "utf-8")) as {
    version: string;
    license: string;
    homepage?: string;
  };
  return {
    name,
    version: manifest.version,
    license: manifest.license,
    homepage: manifest.homepage ?? null,
    licenseText: readFileSync(`${folder}LICENSE`, "utf-8"),
    noticeText: null,
  };
}

// The packages that end up in the build, with their license texts, for the release's third-party notices. Written beside
// dist rather than inside it, so the service does not serve it as part of the interface.
const thirdParty = license({
  thirdParty: {
    allow: {
      test: "(MIT OR Apache-2.0 OR ISC OR BSD-2-Clause OR BSD-3-Clause OR 0BSD OR OFL-1.1)",
      failOnUnlicensed: true,
      failOnViolation: true,
    },
    output: {
      file: fileURLToPath(new URL("./licenses/third-party.json", import.meta.url)),
      template: (dependencies) =>
        JSON.stringify(
          [
            ...dependencies.map((dependency) => ({
              name: dependency.name,
              version: dependency.version,
              license: dependency.license,
              homepage: dependency.homepage,
              licenseText: dependency.licenseText,
              noticeText: dependency.noticeText,
            })),
            ...CSS_PACKAGES.map(cssPackage),
          ],
          null,
          2,
        ),
    },
  },
});

export default defineConfig({
  base: `${UI_BASE}/`,
  plugins: [react(), tailwindcss(), thirdParty],
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
