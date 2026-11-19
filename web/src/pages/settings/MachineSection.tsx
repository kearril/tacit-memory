import { useDictionary } from "../../app/session-context";
import { Setting, type SettingsForm, SettingsSection } from "./fields";

/**
 * The machine section: this machine's name and optional single-line description, prefixed to the resident block and
 * attached to newly written activities without a restart.
 */
export function MachineSection({ form }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.machine;

  return (
    <SettingsSection section="machine" summary={text.summary}>
      <Setting form={form} path="machine.name" text={text.fields.name} />
      <Setting
        form={form}
        path="machine.description"
        text={text.fields.description}
        placeholder={text.descriptionDefault}
        wide
      />
    </SettingsSection>
  );
}
