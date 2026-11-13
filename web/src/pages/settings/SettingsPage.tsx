import { useDictionary } from "../../app/session-context";
import { AccessSection } from "./AccessSection";

/** The settings page: one component per section, and the notice that closes it while some sections are not implemented. */
export function SettingsPage() {
  const dictionary = useDictionary();
  return (
    <div className="grid gap-lg">
      <h1 className="text-xl font-semibold">{dictionary.nav.settings}</h1>
      <AccessSection />
      <p className="text-sm text-ink-muted">{dictionary.notImplemented}</p>
    </div>
  );
}
