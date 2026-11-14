import { useDictionary } from "../../app/session-context";
import { Setting, SettingsGroup, type SettingsForm, SettingsSection } from "./fields";
import { SecretField } from "./SecretField";

/**
 * The backup section: how many automatic backups to keep, then the cloud backup's WebDAV folder, user name and password.
 * Emptying the folder and the user name turns the cloud backup off.
 */
export function BackupSection({ form }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.backup;

  return (
    <SettingsSection section="backup" summary={text.summary}>
      <SettingsGroup title={text.local}>
        <Setting form={form} path="backup.keep" text={text.fields.keep} />
      </SettingsGroup>
      <SettingsGroup title={text.cloud}>
        <Setting form={form} path="backup.webdav.url" text={text.fields.url} placeholder={text.urlDefault} wide />
        <Setting form={form} path="backup.webdav.username" text={text.fields.username} placeholder={text.usernameDefault} />
        <SecretField form={form} path="backup.webdav.password" text={text.fields.password} />
      </SettingsGroup>
    </SettingsSection>
  );
}
