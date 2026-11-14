import { useDictionary } from "../../app/session-context";
import { Setting, SettingsGroup, type SettingsForm, SettingsSection } from "./fields";
import { CONSOLIDATION } from "./form";

/** The consolidation section: its six whole numbers, one row each. */
export function ConsolidationSection({ form }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.consolidation;

  return (
    <SettingsSection section="consolidation" summary={text.summary}>
      <SettingsGroup>
        {CONSOLIDATION.map((key) => (
          <Setting key={key} form={form} path={`consolidation.${key}`} text={text.fields[key]} />
        ))}
      </SettingsGroup>
    </SettingsSection>
  );
}
