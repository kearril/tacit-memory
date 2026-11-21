import {
  Archive,
  Cpu,
  FolderSync,
  Globe,
  Info,
  Layers,
  Link2,
  type LucideIcon,
  Monitor,
  RotateCcw,
  RotateCw,
  SlidersHorizontal,
  Wrench,
} from "lucide-react";
import type { ConfigView } from "../../api/config";
import { useDictionary } from "../../app/session-context";
import { Badge } from "../../components/ui/badge";
import { Card } from "../../components/ui/card";
import { Input } from "../../components/ui/input";
import { Label } from "../../components/ui/label";
import { Tooltip, TooltipContent, TooltipTrigger } from "../../components/ui/tooltip";
import { IconTile } from "../../components/ui/icon-tile";
import type { Explained } from "../../i18n/messages";
import {
  defaultDisplayOf,
  differsFromDefault,
  type Drafts,
  FIELDS,
  inputIdOf,
  outOfRange,
  type Pending,
  type PlacedProblems,
} from "./form";
import { type SectionId, sectionIdOf } from "../../lib/routes";

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
  readonly restoreMany: (paths: readonly string[]) => void;
}

/**
 * The icon of each section, in the head of its card; the models, the backups and the sync take the dashboard's icons for
 * them.
 */
const SECTION_ICONS: Readonly<Record<SectionId, LucideIcon>> = {
  machine: Monitor,
  models: Cpu,
  bindings: Link2,
  judgment: SlidersHorizontal,
  consolidation: Layers,
  backup: Archive,
  sync: FolderSync,
  access: Globe,
  advanced: Wrench,
};

/**
 * One section of the settings page, as an outline card: its icon on a soft tile and its name set into the top border,
 * then its content. A section whose actions happen at once, without the save bar, says so beside its name.
 */
export function SettingsSection({
  section,
  immediate = false,
  action,
  children,
}: {
  readonly section: SectionId;
  readonly immediate?: boolean;
  readonly action?: React.ReactNode;
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
      className="scroll-mt-lg gap-lg"
      label={
        <>
          <IconTile icon={Icon} />
          <h2 id={`${id}-title`} className="text-lg font-semibold">
            {dictionary.settings.sections[section]}
          </h2>
          {immediate ? <Immediate /> : null}
        </>
      }
    >
      {action === undefined || action === null ? null : <div className="flex justify-end">{action}</div>}
      {children}
    </Card>
  );
}

/** Focus indicator shared by focusable non-input triggers wrapped in `Explaining`. */
export const EXPLAIN_FOCUS =
  "rounded-xs outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent";

/** The mark of something that happens at once rather than on save, explained on hover. */
export function Immediate() {
  const dictionary = useDictionary();
  return (
    <Explaining help={dictionary.settings.immediateHelp}>
      <Badge tone="info" tabIndex={0} className={EXPLAIN_FOCUS}>
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
  action,
  children,
}: {
  readonly title?: string;
  readonly note?: string;
  readonly aside?: React.ReactNode;
  readonly action?: React.ReactNode;
  readonly children: React.ReactNode;
}) {
  return (
    <div className="grid gap-sm">
      {title === undefined ? null : (
        <div className="flex flex-wrap items-center gap-sm">
          <h3 className="text-sm font-semibold">{title}</h3>
          {aside}
          {action === undefined || action === null ? null : <div className="ml-auto">{action}</div>}
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
          <span id={id} tabIndex={0} className={`flex w-fit items-center gap-xs text-sm font-medium ${EXPLAIN_FOCUS}`}>
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
  describedBy,
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
  /** Optional extra element id to append to the input's `aria-describedby` (such as an example line under it). */
  readonly describedBy?: string;
  readonly children?: React.ReactNode;
}) {
  const dictionary = useDictionary();
  const id = inputIdOf(path);
  const range = outOfRange(form.view, form.drafts, path);
  const errors = [
    ...(range === null ? [] : [dictionary.settings.outOfRange(String(range.min), String(range.max))]),
    ...(form.problems.byField[path] ?? []),
  ];
  const defaultValue = required || disabled ? null : defaultDisplayOf(form.view, path);
  const differs = defaultValue !== null && differsFromDefault(form.view, form.drafts, path);

  return (
    <SettingRow
      name={<SettingName form={form} path={path} text={text} htmlFor={id} />}
      control={
        <div className="flex min-w-0 items-center gap-sm">
          <div className={wide ? "min-w-0 flex-1" : "w-(--tk-size-tile) max-w-full min-w-0 shrink"}>
            <SettingInput
              form={form}
              path={path}
              id={id}
              label={text.label}
              help={text.help}
              placeholder={placeholder ?? (required ? dictionary.settings.required : undefined)}
              mono={wide}
              disabled={disabled}
              describedBy={describedBy}
              errors={errors}
              errorId={`${id}-error`}
            />
          </div>
          {differs ? (
            <RestoreButton name={text.label} value={defaultValue} focusId={id} onClick={() => form.restore(path)} />
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
      <span tabIndex={0} aria-label={text.restartHelp} className={`inline-flex text-ink-muted ${EXPLAIN_FOCUS}`}>
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
  describedBy,
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
  readonly describedBy?: string;
  readonly errors: readonly string[];
  readonly errorId: string;
}) {
  const changed = form.changed.has(path);
  const kind = FIELDS.find((field) => field.path === path)?.kind;
  const descriptions = [help !== "" ? `${id}-help` : undefined, describedBy, errors.length > 0 ? errorId : undefined]
    .filter((item): item is string => item !== undefined)
    .join(" ");
  return (
    <>
      <Input
        id={id}
        inputMode={kind === "text" ? "url" : kind === "integer" ? "numeric" : "decimal"}
        autoComplete="off"
        spellCheck={false}
        aria-label={label}
        aria-invalid={errors.length > 0}
        aria-describedby={descriptions === "" ? undefined : descriptions}
        placeholder={placeholder}
        disabled={disabled}
        className={`tabular-nums ${mono ? "font-mono sm:text-sm" : ""} ${changed ? "border-accent" : ""}`}
        value={form.drafts[path] ?? ""}
        onChange={(event) => form.edit(path, event.target.value)}
      />
      {help === "" ? null : (
        <span id={`${id}-help`} className="sr-only">
          {help}
        </span>
      )}
    </>
  );
}

/**
 * Compact icon-only button that restores a setting (or threshold pair) to its default, revealing the default value in a
 * tooltip on hover or focus so resting rows stay quiet. Returns keyboard focus to the setting's input as the button unmounts.
 */
export function RestoreButton({
  name,
  value,
  focusId,
  onClick,
}: {
  readonly name: string;
  readonly value: string;
  readonly focusId: string;
  readonly onClick: () => void;
}) {
  const dictionary = useDictionary();
  return (
    <Tooltip>
      <TooltipTrigger asChild>
        <button
          type="button"
          aria-label={dictionary.settings.restoreDefaultLabel(name, value)}
          className="inline-flex min-h-(--tk-size-icon-tile) min-w-(--tk-size-icon-tile) shrink-0 lift cursor-pointer items-center justify-center rounded-sm text-ink-muted outline-none hover:bg-surface-raised hover:text-ink focus-visible:bg-surface-raised focus-visible:text-ink focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent focus-visible:ring-offset-(length:--tk-border-thick) focus-visible:ring-offset-surface pointer-coarse:min-h-control pointer-coarse:min-w-(--tk-size-control)"
          onClick={() => {
            onClick();
            document.getElementById(focusId)?.focus();
          }}
        >
          <RotateCcw aria-hidden className="size-(--tk-font-size-sm)" />
        </button>
      </TooltipTrigger>
      <TooltipContent>{dictionary.settings.restoreDefaultHint(value)}</TooltipContent>
    </Tooltip>
  );
}

/** Quiet button in a tuning group's header that restores every setting in the group back to its default at once. */
export function RestoreGroupButton({
  group,
  focusId,
  onClick,
}: {
  readonly group: string;
  readonly focusId: string;
  readonly onClick: () => void;
}) {
  const dictionary = useDictionary();
  return (
    <button
      type="button"
      aria-label={dictionary.settings.restoreGroupDefaultLabel(group)}
      className="inline-flex min-h-(--tk-size-icon-tile) shrink-0 lift cursor-pointer items-center gap-xs rounded-sm px-sm text-xs font-medium whitespace-nowrap text-ink-muted outline-none hover:bg-surface-raised hover:text-ink focus-visible:bg-surface-raised focus-visible:text-ink focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent focus-visible:ring-offset-(length:--tk-border-thick) focus-visible:ring-offset-surface pointer-coarse:min-h-control"
      onClick={() => {
        onClick();
        document.getElementById(focusId)?.focus();
      }}
    >
      <RotateCcw aria-hidden className="size-(--tk-font-size-xs)" />
      {dictionary.settings.restoreGroupDefault}
    </button>
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
  if (help === "" && keyPath === undefined) {
    return children;
  }
  return (
    <Tooltip>
      <TooltipTrigger asChild>{children}</TooltipTrigger>
      <TooltipContent>
        <span className="grid gap-xs">
          {help === "" ? null : <span>{help}</span>}
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
