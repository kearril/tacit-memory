import { Eye, EyeOff, KeyRound, Lock, PenLine, Trash2, Undo2 } from "lucide-react";
import { useState } from "react";
import { getSecret } from "../../api/config";
import { useDictionary } from "../../app/session-context";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import { PasswordInput } from "../../components/ui/password-input";
import { errorTextOf, type Explained } from "../../i18n/messages";
import { Explaining, Problems, type SettingsForm } from "./fields";
import { inputIdOf } from "./form";

/**
 * A secret: its name and key path with where its value comes from, then a box that keeps the value hidden until "Show"
 * fetches it. A secret from its environment variable is read-only here and says which variable to change. One from
 * `config.yaml`, or one not set, can be changed (an input takes the new value) and one in the file can be cleared; either
 * counts as a change of the page until saved or undone.
 */
export function SecretField({ form, path, text }: { readonly form: SettingsForm; readonly path: string; readonly text: Explained }) {
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

  return (
    <div className="relative grid gap-xs pl-md">
      {edit === undefined ? null : (
        <span aria-hidden className="absolute inset-y-0 left-0 w-(--tk-border-thick) rounded-full bg-accent" />
      )}
      <div className="flex min-w-0 flex-wrap items-baseline gap-x-sm gap-y-xs">
        <Explaining help={text.help}>
          <span className="text-sm font-medium">{text.label}</span>
        </Explaining>
        <code className="min-w-0 font-mono text-xs break-all text-ink-muted">{path}</code>
        <Badge tone={source === null ? "neutral" : "accent"}>
          {source === "environment" ? <Lock aria-hidden /> : <KeyRound aria-hidden />}
          {source === "environment" ? words.fromEnvironment(variable) : source === "configFile" ? words.fromFile : words.unset}
        </Badge>
      </div>

      {typeof edit === "string" ? (
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
              {edit === null ? words.willClear : shown !== null ? shown || words.unset : source === null ? words.unset : "••••••••"}
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
          ) : source === "environment" ? null : (
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
      )}

      <p className="text-xs text-ink-muted">
        {source === "environment" ? words.environmentNote(variable) : words.fileNote(variable)}
      </p>
      {failure === null ? null : (
        <p role="alert" className="text-sm text-danger">
          {failure}
        </p>
      )}
      <Problems id={`${id}-error`} messages={errors} />
    </div>
  );
}
