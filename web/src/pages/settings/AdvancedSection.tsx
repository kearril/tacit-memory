import { useDictionary } from "../../app/session-context";
import { ReadOnlySetting, Setting, SettingsGroup, type SettingsForm, SettingsSection } from "./fields";
import { effective, textOf } from "./form";

/**
 * The advanced section: the port and the limits on briefs and detail pages, then the memory repository and language,
 * which only `config.yaml` changes, as plain rows under one note.
 */
export function AdvancedSection({ form }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.advanced;
  const language = textOf(effective(form.view, "file", "language"));

  return (
    <SettingsSection section="advanced" summary={text.summary}>
      <div className="grid gap-md">
        <Setting form={form} path="server.port" text={text.fields.port} />
        <Setting form={form} path="limits.brief" text={text.fields.brief} />
        <Setting form={form} path="limits.page.minAliases" text={text.fields.minAliases} />
      </div>
      <SettingsGroup title={text.fileOnly}>
        <p className="text-xs text-ink-muted">{dictionary.settings.readOnlyNote}</p>
        <ReadOnlySetting
          form={form}
          path="memory.path"
          text={text.fields.memoryPath}
          value={textOf(effective(form.view, "file", "memory.path"))}
        />
        <ReadOnlySetting form={form} path="language" text={text.fields.language} value={text.languages[language] ?? language} />
      </SettingsGroup>
    </SettingsSection>
  );
}
