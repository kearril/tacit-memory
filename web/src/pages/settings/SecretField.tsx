import { Lock } from "lucide-react";
import { useDictionary } from "../../app/session-context";
import { PasswordInput } from "../../components/ui/password-input";
import type { Explained } from "../../i18n/messages";
import { Explaining, Problems, SettingName, SettingRow, type SettingsForm } from "./fields";
import { inputIdOf } from "./form";

/**
 * A secret as a row of the section. One from its environment variable is read-only here: the row only names the variable,
 * and hovering it says how to change it. Otherwise it is an input like any setting's, masked: it holds what `config.yaml`
 * holds, a change counts as one of the page's until saved or discarded, and emptying it removes the secret on save.
 */
export function SecretField({
  form,
  path,
  text,
}: {
  readonly form: SettingsForm;
  readonly path: string;
  readonly text: Explained;
}) {
  const dictionary = useDictionary();
  const words = dictionary.settings.secret;
  const id = inputIdOf(path);
  const state = form.view.secrets.find((secret) => secret.path === path);
  const source = state?.source ?? null;
  const variable = state?.variable ?? "";
  const draft = form.drafts[path] ?? "";
  const errors = form.problems.byField[path] ?? [];

  if (source === "environment") {
    return (
      <SettingRow
        name={<SettingName form={form} path={path} text={text} id={`${id}-label`} />}
        control={
          <Explaining help={words.environmentNote(variable)}>
            <p
              id={id}
              tabIndex={0}
              className="flex min-h-control w-fit items-center gap-sm text-sm text-ink-muted outline-none"
            >
              <Lock aria-hidden className="size-(--tk-font-size-sm)" />
              {words.fromEnvironment(variable)}
            </p>
          </Explaining>
        }
      >
        <Problems id={`${id}-error`} messages={errors} />
      </SettingRow>
    );
  }

  const emptied = draft.trim() === "";
  return (
    <SettingRow
      name={<SettingName form={form} path={path} text={text} htmlFor={id} />}
      control={
        <PasswordInput
          id={id}
          autoComplete="new-password"
          placeholder={words.unset}
          showLabel={dictionary.showPassword(text.label)}
          hideLabel={dictionary.hidePassword(text.label)}
          aria-invalid={errors.length > 0}
          aria-describedby={errors.length > 0 ? `${id}-error` : undefined}
          className={`font-mono ${form.changed.has(path) ? "border-accent" : ""}`}
          value={draft}
          onChange={(event) => form.edit(path, event.target.value)}
        />
      }
    >
      <p className="text-xs text-ink-muted">
        {!emptied ? words.fileNote(variable) : source === null ? words.unsetNote(variable) : words.willClear}
      </p>
      <Problems id={`${id}-error`} messages={errors} />
    </SettingRow>
  );
}
