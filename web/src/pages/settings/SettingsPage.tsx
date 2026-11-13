import { useDictionary } from "../../app/session-context";
import { AccessSection } from "./AccessSection";

/**
 * The settings page: the sections as cards in a two-column grid on a wide screen and one column on a narrow one. The
 * sections that are not implemented yet are held by one dashed placeholder. The page heading is for screen readers only,
 * as the sidebar already shows which page this is.
 */
export function SettingsPage() {
  const dictionary = useDictionary();
  return (
    <div className="grid items-stretch gap-lg md:grid-cols-2">
      <h1 className="sr-only">{dictionary.nav.settings}</h1>
      <AccessSection />
      <div className="grid place-items-center rounded-lg border-(length:--tk-border-thick) border-dashed border-line p-lg text-sm text-ink-muted">
        {dictionary.notImplemented}
      </div>
    </div>
  );
}
