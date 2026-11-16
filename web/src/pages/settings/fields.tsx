import {
  Archive,
  Cpu,
  Globe,
  Info,
  Layers,
  Link2,
  type LucideIcon,
  RotateCcw,
  RotateCw,
  SlidersHorizontal,
  Wrench,
} from "lucide-react";
import type { ConfigView } from "../../api/config";
import { useDictionary } from "../../app/session-context";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import { Card } from "../../components/ui/card";
import { Input } from "../../components/ui/input";
import { Label } from "../../components/ui/label";
import { Tooltip, TooltipContent, TooltipTrigger } from "../../components/ui/tooltip";
import type { Explained } from "../../i18n/messages";
import {
  differsFromDefault,
  type Drafts,
  FIELDS,
  inputIdOf,
  outOfRange,
  type Pending,
  type PlacedProblems,
  type SecretEdits,
  type SectionId,
  sectionIdOf,
  textOf,
  valueAt,
} from "./form";

/** What a section needs to show and edit its settings: the view, the inputs, what changed, and where the problems are. */
export interface SettingsForm {
  readonly view: ConfigView;
  readonly drafts: Drafts;
  readonly changed: ReadonlySet<string>;
  readonly problems: PlacedProblems;
  /** The settings saved with a value the service takes only when it restarts, by key path. */
  readonly pending: ReadonlyMap<string, Pending>;
  readonly edit: (path: string, text: string) => void;
  readonly restore: (path: string) => void;
  /** The secrets changed on the page; see `SecretEdits`. */
  readonly secrets: SecretEdits;
  /** Rewrites a secret (a text), clears it (`null`), or undoes either (`undefined`). */
  readonly editSecret: (path: string, value: string | null | undefined) => void;
}

/** The icon of each section, in the head of its card; the models and the backups take the dashboard's icons for them. */
const SECTION_ICONS: Readonly<Record<SectionId, LucideIcon>> = {
  models: Cpu,
  bindings: Link2,
  judgment: SlidersHorizontal,
  consolidation: Layers,
  backup: Archive,
  access: Globe,
  advanced: Wrench,
};

/**
 * One section of the settings page, as an outline card: its icon on a soft tile and its name set into the top border, a
 * line on what it is for, then its content. A section whose actions happen at once, without the save bar, says so beside its name.
 */
export function SettingsSection({
  section,
  summary,
  immediate = false,
  children,
}: {
  readonly section: SectionId;
  readonly summary: string;
  readonly immediate?: boolean;
  readonly children: React.ReactNode;
}) {
  const dictionary = useDictionary();
  const id = sectionIdOf(section);
  const Icon = SECTION_ICONS[section];
  return (
    <Card
      id={id}
      aria-labelledby={`${id}-title`}
      variant="outline"
      className="scroll-mt-lg gap-xl"
      label={
        <>
          <span className="grid size-(--tk-size-icon-tile) place-items-center rounded-sm bg-accent-soft text-accent">
            <Icon aria-hidden className="size-(--tk-font-size-sm)" />
          </span>
          <h2 id={`${id}-title`} className="text-lg font-semibold">
            {dictionary.settings.sections[section]}
          </h2>
          {immediate ? <Immediate /> : null}
        </>
      }
    >
      <p className="text-sm text-ink-muted">{summary}</p>
      {children}
    </Card>
  );
}

/** The mark of something that happens at once rather than on save, explained on hover. */
export function Immediate() {
  const dictionary = useDictionary();
  return (
    <Explaining help={dictionary.settings.immediateHelp}>
      <Badge tone="info" tabIndex={0}>
        {dictionary.settings.immediate}
      </Badge>
    </Explaining>
  );
}

/**
 * A group of rows inside a section: a small heading of its own when it has one, a note under it when it needs one, then the
 * rows. The group has no box and no lines; the section's card is the only one.
 */
export function SettingsGroup({
  title,
  note,
  aside,
  children,
}: {
  readonly title?: string;
  readonly note?: string;
  readonly aside?: React.ReactNode;
  readonly children: React.ReactNode;
}) {
  return (
    <div className="grid gap-sm">
      {title === undefined ? null : (
        <div className="flex flex-wrap items-center gap-sm">
          <h3 className="text-sm font-semibold">{title}</h3>
          {aside}
        </div>
      )}
      {note === undefined ? null : <p className="text-xs text-ink-muted">{note}</p>}
      <Rows>{children}</Rows>
    </div>
  );
}

/** Rows one under another, set apart by space alone. */
export function Rows({ children }: { readonly children: React.ReactNode }) {
  return <div className="grid gap-md">{children}</div>;
}

/**
 * A row of a section: the name on the left and what sets it on the right, stacked on a narrow screen. Anything under the
 * control (a note, the problems) lines up with the control.
 */
export function SettingRow({
  name,
  control,
  children,
}: {
  readonly name: React.ReactNode;
  readonly control: React.ReactNode;
  readonly children?: React.ReactNode;
}) {
  return (
    <div className="grid items-start gap-x-lg gap-y-xs sm:grid-cols-[minmax(0,var(--tk-size-setting-name))_minmax(0,1fr)]">
      <div className="flex min-h-control min-w-0 flex-wrap items-center gap-x-sm gap-y-xs">{name}</div>
      <div className="grid min-w-0 gap-xs">
        {control}
        {children}
      </div>
    </div>
  );
}

/**
 * The settings whose names carry a mark that they explain themselves: only those whose explanation says something the name
 * and the input do not, so the marks stay few. Every name still explains itself on hover.
 */
const EXPLAINED = new Set([
  "server.host",
  "server.port",
  "retrieval.minRerankScore",
  "consolidation.budgetSeconds",
  "backup.keep",
]);

/**
 * A setting's name: hovering it explains the setting and gives its key path in `config.yaml`; the few whose explanation
 * matters carry a small mark that says so. A setting that takes effect
 * only when the service restarts has a small mark after it; one saved with a value still waiting for a restart has a badge
 * with the running value instead.
 */
export function SettingName({
  form,
  path,
  text,
  htmlFor,
  id,
}: {
  readonly form: SettingsForm;
  readonly path: string;
  readonly text: Explained;
  readonly htmlFor?: string;
  readonly id?: string;
}) {
  const mark = EXPLAINED.has(path) ? <Info aria-hidden className="size-(--tk-font-size-sm) text-ink-muted" /> : null;
  return (
    <>
      <Explaining help={text.help} keyPath={path}>
        {htmlFor === undefined ? (
          <span id={id} tabIndex={0} className="flex w-fit items-center gap-xs text-sm font-medium outline-none">
            {text.label}
            {mark}
          </span>
        ) : (
          <Label id={id} htmlFor={htmlFor} className="w-fit gap-xs">
            {text.label}
            {mark}
          </Label>
        )}
      </Explaining>
      <RestartMark form={form} path={path} />
    </>
  );
}

/**
 * One setting: its name, the input, and once the value differs from the default a button that restores it, naming the
 * default. Below the input, a number outside its limits and the service's problems with the setting. `placeholder` says
 * what an empty input means, for a setting whose default is the absence of a value.
 */
export function Setting({
  form,
  path,
  text,
  placeholder,
  wide = false,
  required = false,
  disabled = false,
  children,
}: {
  readonly form: SettingsForm;
  readonly path: string;
  readonly text: Explained;
  readonly placeholder?: string;
  /** Whether the input takes the whole width, for an address rather than a number. */
  readonly wide?: boolean;
  /** Whether the setting has no default to restore, so it must be filled in. */
  readonly required?: boolean;
  readonly disabled?: boolean;
  readonly children?: React.ReactNode;
}) {
  const dictionary = useDictionary();
  const id = inputIdOf(path);
  const range = outOfRange(form.view, form.drafts, path);
  const errors = [
    ...(range === null ? [] : [dictionary.settings.outOfRange(String(range.min), String(range.max))]),
    ...(form.problems.byField[path] ?? []),
  ];
  const differs = !required && differsFromDefault(form.view, form.drafts, path);

  return (
    <SettingRow
      name={<SettingName form={form} path={path} text={text} htmlFor={id} />}
      control={
        <div className="flex min-w-0 flex-wrap items-center gap-sm">
          <div className={wide ? "min-w-0 flex-1" : "w-(--tk-size-tile) max-w-full"}>
            <SettingInput
              form={form}
              path={path}
              id={id}
              label={text.label}
              help={text.help}
              placeholder={placeholder ?? (required ? dictionary.settings.required : undefined)}
              mono={wide}
              disabled={disabled}
              errors={errors}
              errorId={`${id}-error`}
            />
          </div>
          {differs ? (
            <RestoreButton
              label={`${dictionary.settings.restoreDefault}: ${text.label}`}
              value={placeholder === undefined ? textOf(valueAt(form.view.defaults, path)) : null}
              onClick={() => form.restore(path)}
            />
          ) : null}
        </div>
      }
    >
      {children}
      <Problems id={`${id}-error`} messages={errors} />
    </SettingRow>
  );
}

/**
 * A setting the page shows but does not change, as a plain row of its name and value. The service's problems with it
 * show as with any setting.
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
  const id = inputIdOf(path);
  const errors = form.problems.byField[path] ?? [];
  return (
    <SettingRow
      name={<SettingName form={form} path={path} text={text} />}
      control={
        <p
          id={id}
          tabIndex={-1}
          className="flex min-h-control items-center text-sm break-all text-ink-muted outline-none"
        >
          {value}
        </p>
      }
    >
      <Problems id={`${id}-error`} messages={errors} />
    </SettingRow>
  );
}

/**
 * After a setting's name: a badge with the running value while a saved value waits for a restart, else a quiet mark for
 * a setting that takes effect only on a restart, explained on hover.
 */
export function RestartMark({ form, path }: { readonly form: SettingsForm; readonly path: string }) {
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
      <span tabIndex={0} aria-label={text.restartHelp} className="inline-flex text-ink-muted outline-none">
        <RotateCw aria-hidden className="size-(--tk-font-size-sm)" />
      </span>
    </Explaining>
  ) : null;
}

/** The input of one setting: the text as typed, outlined in the accent colour once it differs from the saved value. */
export function SettingInput({
  form,
  path,
  id,
  label,
  help,
  placeholder,
  mono = false,
  disabled = false,
  errors,
  errorId,
}: {
  readonly form: SettingsForm;
  readonly path: string;
  readonly id: string;
  readonly label: string;
  readonly help: string;
  readonly placeholder?: string;
  /** Whether the value is an address or a path, set in the monospaced face. */
  readonly mono?: boolean;
  readonly disabled?: boolean;
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
        placeholder={placeholder}
        disabled={disabled}
        className={`tabular-nums ${mono ? "font-mono" : ""} ${changed ? "border-accent" : ""}`}
        value={form.drafts[path] ?? ""}
        onChange={(event) => form.edit(path, event.target.value)}
      />
      <span id={`${id}-help`} className="sr-only">
        {help}
      </span>
    </>
  );
}

/** Restores a setting's default, naming the default when it has one to show. */
export function RestoreButton({
  label,
  value,
  onClick,
}: {
  readonly label: string;
  readonly value: string | null;
  readonly onClick: () => void;
}) {
  const dictionary = useDictionary();
  return (
    <Button type="button" variant="ghost" aria-label={label} title={label} className="text-ink-muted" onClick={onClick}>
      <RotateCcw aria-hidden />
      {value === null ? dictionary.settings.restoreDefault : dictionary.settings.defaultValue(value)}
    </Button>
  );
}

/** A name that explains itself on hover, with the key path in `config.yaml` under the explanation when there is one. */
export function Explaining({
  help,
  keyPath,
  children,
}: {
  readonly help: string;
  readonly keyPath?: string;
  readonly children: React.ReactNode;
}) {
  return (
    <Tooltip>
      <TooltipTrigger asChild>{children}</TooltipTrigger>
      <TooltipContent>
        <span className="grid gap-xs">
          <span>{help}</span>
          {keyPath === undefined ? null : <code className="font-mono text-xs">{keyPath}</code>}
        </span>
      </TooltipContent>
    </Tooltip>
  );
}

/** The problems with a setting: the service's, in its words, and any the page finds before saving. */
export function Problems({ id, messages }: { readonly id: string; readonly messages: readonly string[] }) {
  return messages.length === 0 ? null : (
    <div id={id} role="alert" className="grid gap-xs text-sm text-danger">
      {messages.map((message, index) => (
        <p key={index}>{message}</p>
      ))}
    </div>
  );
}
