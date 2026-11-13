import { Lock, RotateCcw, RotateCw } from "lucide-react";
import type { ConfigView } from "../../api/config";
import { useDictionary } from "../../app/session-context";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
import { Label } from "../../components/ui/label";
import { Tooltip, TooltipContent, TooltipTrigger } from "../../components/ui/tooltip";
import type { Explained } from "../../i18n/messages";
import { differsFromDefault, type Drafts, FIELDS, inputIdOf, type Pending, type PlacedProblems, textOf, valueAt } from "./form";

/** What a card needs to show and edit its settings: the view, the inputs, what changed, and where the problems are. */
export interface SettingsForm {
  readonly view: ConfigView;
  readonly drafts: Drafts;
  readonly changed: ReadonlySet<string>;
  readonly problems: PlacedProblems;
  /** The settings saved with a value the service takes only when it restarts, by key path. */
  readonly pending: ReadonlyMap<string, Pending>;
  readonly edit: (path: string, text: string) => void;
  readonly restore: (path: string) => void;
}

/**
 * One setting: its name (hovering it explains the setting) beside its key path and whether it waits for a restart, the
 * input with the restore button after it, the default and the range below, anything the card adds (`children`), and the
 * service's problems with it. A setting that differs from its default has an accent bar on its left; an input changed
 * since the last save has an accent outline.
 */
export function Setting({
  form,
  path,
  text,
  defaultText,
  children,
}: {
  readonly form: SettingsForm;
  readonly path: string;
  readonly text: Explained;
  /** How to name the default, for a setting whose default is the absence of a value. */
  readonly defaultText?: string;
  readonly children?: React.ReactNode;
}) {
  const dictionary = useDictionary();
  const id = inputIdOf(path);
  const key = form.view.keys.find((key) => key.path === path);
  const errors = form.problems.byField[path] ?? [];
  const differs = differsFromDefault(form.view, form.drafts, path);
  const notes = [defaultText ?? dictionary.settings.defaultValue(textOf(valueAt(form.view.defaults, path)))];
  if (key?.min != null && key.max != null) {
    notes.push(dictionary.settings.range(String(key.min), String(key.max)));
  }

  return (
    <div className="relative grid gap-xs pl-md">
      <DefaultBar shown={differs} />
      <div className="flex min-w-0 flex-wrap items-baseline gap-x-sm">
        <Explaining help={text.help}>
          <Label htmlFor={id}>{text.label}</Label>
        </Explaining>
        <code className="truncate font-mono text-xs text-ink-muted">{path}</code>
        <RestartBadge form={form} path={path} />
      </div>
      <div className="flex max-w-(--tk-size-tile) items-center gap-xs">
        <SettingInput form={form} path={path} id={id} label={text.label} help={text.help} errors={errors} errorId={`${id}-error`} />
        <RestoreButton shown={differs} label={`${dictionary.settings.restoreDefault}: ${text.label}`} onClick={() => form.restore(path)} />
      </div>
      <p className="text-xs text-ink-muted">{notes.join(" · ")}</p>
      {children}
      <Problems id={`${id}-error`} messages={errors} />
    </div>
  );
}

/**
 * A setting the page shows but does not change: its name and key path, its value in a locked box, and how to change it.
 * The service's problems with it show as with any setting.
 */
export function ReadOnlySetting({
  form,
  path,
  text,
  value,
}: {
  readonly form: SettingsForm;
  readonly path: string;
  readonly text: Explained;
  readonly value: string;
}) {
  const dictionary = useDictionary();
  const id = inputIdOf(path);
  const errors = form.problems.byField[path] ?? [];
  return (
    <div className="grid gap-xs pl-md">
      <div className="flex min-w-0 flex-wrap items-baseline gap-x-sm">
        <Explaining help={text.help}>
          <span className="text-sm font-medium">{text.label}</span>
        </Explaining>
        <code className="truncate font-mono text-xs text-ink-muted">{path}</code>
        <RestartBadge form={form} path={path} />
      </div>
      <p
        id={id}
        tabIndex={-1}
        className="flex min-h-control min-w-0 items-start gap-sm rounded-md bg-surface-raised px-md py-sm text-sm text-ink-muted outline-none"
      >
        <Lock aria-hidden className="mt-xs size-(--tk-font-size-sm) shrink-0" />
        <span className="min-w-0 font-mono break-all">{value}</span>
      </p>
      <p className="text-xs text-ink-muted">{dictionary.settings.readOnlyNote}</p>
      <Problems id={`${id}-error`} messages={errors} />
    </div>
  );
}

/**
 * Whether a setting waits for a restart: a quiet badge for one that does, and a warning badge with the running value
 * once a saved value is waiting.
 */
export function RestartBadge({ form, path }: { readonly form: SettingsForm; readonly path: string }) {
  const dictionary = useDictionary();
  const text = dictionary.settings;
  const pending = form.pending.get(path);
  if (pending !== undefined) {
    return (
      <Badge tone="warning">
        <RotateCw aria-hidden />
        {text.pendingBadge(pending.running)}
      </Badge>
    );
  }
  return form.view.keys.some((key) => key.path === path && key.restart && !key.readOnly) ? (
    <Explaining help={text.restartHelp}>
      <Badge>
        <RotateCw aria-hidden />
        {text.restartBadge}
      </Badge>
    </Explaining>
  ) : null;
}

/** The input of one number: the text as typed, outlined in the accent colour once it differs from the saved value. */
export function SettingInput({
  form,
  path,
  id,
  label,
  help,
  errors,
  errorId,
}: {
  readonly form: SettingsForm;
  readonly path: string;
  readonly id: string;
  readonly label: string;
  readonly help: string;
  readonly errors: readonly string[];
  readonly errorId: string;
}) {
  const changed = form.changed.has(path);
  const kind = FIELDS.find((field) => field.path === path)?.kind;
  return (
    <>
      <Input
        id={id}
        inputMode={kind === "text" ? "url" : kind === "integer" ? "numeric" : "decimal"}
        autoComplete="off"
        spellCheck={false}
        aria-label={label}
        aria-invalid={errors.length > 0}
        aria-describedby={errors.length > 0 ? `${id}-help ${errorId}` : `${id}-help`}
        className={`tabular-nums ${changed ? "border-accent" : ""}`}
        value={form.drafts[path] ?? ""}
        onChange={(event) => form.edit(path, event.target.value)}
      />
      <span id={`${id}-help`} className="sr-only">
        {help}
      </span>
    </>
  );
}

/** The accent bar on the left of a setting that differs from its default. */
export function DefaultBar({ shown }: { readonly shown: boolean }) {
  return shown ? <span aria-hidden className="absolute inset-y-0 left-0 w-(--tk-border-thick) rounded-full bg-accent" /> : null;
}

/** Restores a setting's default; it keeps its place while hidden, so the inputs beside it do not move. */
export function RestoreButton({ shown, label, onClick }: { readonly shown: boolean; readonly label: string; readonly onClick: () => void }) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <Button
          type="button"
          variant="ghost"
          size="icon"
          aria-label={label}
          aria-hidden={!shown}
          tabIndex={shown ? 0 : -1}
          className={shown ? "text-ink-muted" : "invisible"}
          onClick={onClick}
        >
          <RotateCcw aria-hidden />
        </Button>
      </TooltipTrigger>
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
  );
}

/** A name that explains itself on hover. */
export function Explaining({ help, children }: { readonly help: string; readonly children: React.ReactNode }) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>{children}</TooltipTrigger>
      <TooltipContent>{help}</TooltipContent>
    </Tooltip>
  );
}

/** The service's problems with a setting, in its words. */
export function Problems({ id, messages }: { readonly id: string; readonly messages: readonly string[] }) {
  return messages.length === 0 ? null : (
    <div id={id} role="alert" className="grid gap-xs text-sm text-danger">
      {messages.map((message, index) => (
        <p key={index}>{message}</p>
      ))}
    </div>
  );
}
