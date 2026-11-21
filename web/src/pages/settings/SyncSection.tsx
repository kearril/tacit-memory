import { useDictionary } from "../../app/session-context";
import { Setting, SettingsGroup, type SettingsForm, SettingsSection } from "./fields";

/**
 * The sync section: the remote the memory repository syncs with, whose emptying turns the sync off, and how often it is
 * pulled. Choosing a side after a pause is left to `tacit sync`.
 */
export function SyncSection({ form }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.sync;

  return (
    <SettingsSection section="sync">
      <SettingsGroup>
        <Setting form={form} path="sync.remote" text={text.fields.remote} placeholder={text.remoteDefault} wide />
        <Setting form={form} path="sync.pullMinutes" text={text.fields.pullMinutes} />
      </SettingsGroup>
    </SettingsSection>
  );
}
