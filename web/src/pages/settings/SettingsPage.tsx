import { useDictionary } from "../../app/session-context";
import { AccessSection } from "./AccessSection";

/** The settings page. Only its access section exists in this stage; the rest of it is not implemented yet. */
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
