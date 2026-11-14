import { Eye, EyeOff, Lock, PenLine, Trash2, Undo2 } from "lucide-react";
import { useState } from "react";
import { getSecret } from "../../api/config";
import { useDictionary } from "../../app/session-context";
import { Button } from "../../components/ui/button";
import { PasswordInput } from "../../components/ui/password-input";
import { errorTextOf, type Explained } from "../../i18n/messages";
import { Explaining, Problems, SettingName, SettingRow, type SettingsForm } from "./fields";
import { inputIdOf } from "./form";

/**
 * A secret as a row of the section. One from its environment variable is read-only here: the row only names the variable,
 * and hovering it says how to change it. One from `config.yaml`, or one not set, shows a box that keeps the value hidden
 * until "Show" fetches it; it can be changed (an input takes the new value) and one in the file can be cleared, either
 * counting as a change of the page until saved or undone.
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
  const edit = form.secrets[path];
  const errors = form.problems.byField[path] ?? [];
  const [shown, setShown] = useState<string | null>(null);
  const [showing, setShowing] = useState(false);
  const [failure, setFailure] = useState<string | null>(null);

  const toggle = async () => {
    if (shown !== null) {
      setShown(null);
      return;
    }
    setShowing(true);
    setFailure(null);
    try {
      setShown((await getSecret(path)).value ?? "");
    } catch (error) {
      setFailure(errorTextOf(error, dictionary));
    } finally {
      setShowing(false);
    }
  };

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

  const control =
    typeof edit === "string" ? (
      <div className="flex items-center gap-xs">
        <div className="min-w-0 flex-1">
          <PasswordInput
            id={id}
            autoComplete="off"
            autoFocus
            placeholder={words.newValue}
            showLabel={words.show}
            hideLabel={words.hide}
            aria-label={`${text.label}: ${words.newValue}`}
            aria-invalid={errors.length > 0}
            className="border-accent"
            value={edit}
            onChange={(event) => form.editSecret(path, event.target.value)}
          />
        </div>
        <Button type="button" variant="ghost" onClick={() => form.editSecret(path, undefined)}>
          <Undo2 aria-hidden />
          {words.undo}
        </Button>
      </div>
    ) : (
      <div className="flex items-center gap-xs">
        <p
          id={id}
          tabIndex={-1}
          className={`flex min-h-control min-w-0 flex-1 items-center rounded-md bg-surface-raised px-md font-mono text-sm outline-none ${
            edit === null ? "text-ink-muted line-through" : shown === null ? "text-ink-muted" : "text-ink"
          }`}
        >
          <span className="min-w-0 break-all">
            {edit === null
              ? words.willClear
              : shown !== null
                ? shown || words.unset
                : source === null
                  ? words.unset
                  : "••••••••"}
          </span>
        </p>
        {source === null || edit === null ? null : (
          <Button
            type="button"
            variant="outline"
            size="icon"
            aria-label={shown === null ? words.show : words.hide}
            aria-pressed={shown !== null}
            disabled={showing}
            onClick={() => void toggle()}
          >
            {shown === null ? <Eye aria-hidden /> : <EyeOff aria-hidden />}
          </Button>
        )}
        {edit === null ? (
          <Button type="button" variant="ghost" onClick={() => form.editSecret(path, undefined)}>
            <Undo2 aria-hidden />
            {words.undo}
          </Button>
        ) : (
          <>
            <Button type="button" variant="outline" onClick={() => form.editSecret(path, "")}>
              <PenLine aria-hidden />
              {words.rewrite}
            </Button>
            {source === "configFile" ? (
              <Button type="button" variant="ghost" onClick={() => form.editSecret(path, null)}>
                <Trash2 aria-hidden />
                {words.clear}
              </Button>
            ) : null}
          </>
        )}
      </div>
    );

  return (
    <SettingRow name={<SettingName form={form} path={path} text={text} id={`${id}-label`} />} control={control}>
      {edit === undefined && source === null ? (
        <p className="text-xs text-ink-muted">{words.unsetNote(variable)}</p>
      ) : null}
      {source === "configFile" || typeof edit === "string" ? (
        <p className="text-xs text-ink-muted">{words.fileNote(variable)}</p>
      ) : null}
      {failure === null ? null : (
        <p role="alert" className="text-sm text-danger">
          {failure}
        </p>
      )}
      <Problems id={`${id}-error`} messages={errors} />
    </SettingRow>
  );
}
