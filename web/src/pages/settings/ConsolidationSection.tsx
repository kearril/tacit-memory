import { useDictionary } from "../../app/session-context";
import { Setting, type SettingsForm, SettingsSection } from "./fields";
import { CONSOLIDATION } from "./form";

/** The consolidation section: its six whole numbers, one row each. */
export function ConsolidationSection({ form }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.consolidation;

  return (
    <SettingsSection section="consolidation" summary={text.summary}>
      <div className="grid gap-md">
        {CONSOLIDATION.map((key) => (
          <Setting key={key} form={form} path={`consolidation.${key}`} text={text.fields[key]} />
        ))}
      </div>
    </SettingsSection>
  );
}
