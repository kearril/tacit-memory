import { useDictionary } from "../../app/session-context";
import { RestoreGroupButton, Setting, SettingsGroup, type SettingsForm, SettingsSection } from "./fields";
import { CONSOLIDATION, CONSOLIDATION_PATHS, differedCount, inputIdOf } from "./form";

/** The consolidation section: its six whole numbers, one row each. */
export function ConsolidationSection({ form }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.consolidation;
  const groupDiffers = differedCount(form.view, form.drafts, CONSOLIDATION_PATHS) >= 2;

  return (
    <SettingsSection
      section="consolidation"
      action={
        groupDiffers ? (
          <RestoreGroupButton
            group={dictionary.settings.sections.consolidation}
            focusId={inputIdOf(CONSOLIDATION_PATHS[0])}
            onClick={() => form.restoreMany(CONSOLIDATION_PATHS)}
          />
        ) : null
      }
    >
      <SettingsGroup>
        {CONSOLIDATION.map((key) => (
          <Setting key={key} form={form} path={`consolidation.${key}`} text={text.fields[key]} />
        ))}
      </SettingsGroup>
    </SettingsSection>
  );
}
