import { useDictionary } from "../app/session-context";
import type { Dictionary } from "../i18n/messages";

/** A page whose content is not part of this stage: its name, and the sentence that says so. */
export function PlaceholderPage({ section }: { readonly section: keyof Dictionary["nav"] }) {
  const dictionary = useDictionary();
  return (
    <div className="grid gap-md">
      <h1 className="text-xl font-semibold">{dictionary.nav[section]}</h1>
      <p className="text-sm text-ink-muted">{dictionary.notImplemented}</p>
    </div>
  );
}
