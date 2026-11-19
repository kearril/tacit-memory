import type { Dictionary } from "../i18n/messages";
import { routeOf } from "../lib/routes";

/** Browser tab title for `pathname` in the current memory language, front-loading the view name. */
export function pageTitleOf(pathname: string, dictionary: Dictionary): string {
  const parts = titlePartsOf(pathname, dictionary);
  return parts.length === 0 ? dictionary.appName : [...parts, dictionary.appName].join(" · ");
}

function titlePartsOf(pathname: string, dictionary: Dictionary): readonly string[] {
  const route = routeOf(pathname);
  switch (route.kind) {
    case "login":
      return [dictionary.login.title];
    case "dashboard":
      return [dictionary.nav.dashboard];
    case "settings":
      return [dictionary.nav.settings];
    case "prompts":
      return [route.name === undefined ? dictionary.nav.prompts : dictionary.prompts.documents[route.name].title];
    case "logs":
      return route.date === undefined ? [dictionary.nav.logs] : [route.date, dictionary.nav.logs];
    case "memory": {
      if (route.view === undefined) {
        return [dictionary.nav.memory];
      }
      const viewLabel = dictionary.memory.views[route.view];
      return route.target === undefined ? [viewLabel] : [route.target, viewLabel];
    }
    case "unknown":
      return [];
  }
}
