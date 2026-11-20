import type { ReactNode } from "react";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Activity, ChevronDown } from "lucide-react";
import { useState } from "react";
import { afterCommand } from "../../api/effects";
import { PROBE_AFFECTS, runProbe } from "../../api/probe";
import { useDictionary } from "../../app/session-context";
import { Button } from "../../components/ui/button";
import { errorTextOf } from "../../i18n/messages";
import { BusyIcon } from "../../components/ui/busy-icon";
import { ProbeLine } from "../../components/probe-line";
import { Problems, Rows, Setting, SettingName, SettingRow, type SettingsForm, SettingsSection } from "./fields";
import { inputIdOf, MODELS, ON } from "./form";
import { SecretField } from "./SecretField";

type Model = (typeof MODELS)[number];

/**
 * The models section: the three auxiliary models one under another, each a line that reads at a glance (its switch, its
 * name, and the model, protocol and URL while it is on) with an edit button that opens its settings under it, inset. One
 * opens at a time, and any with a problem opens too, so the problem can be seen. A model switched off keeps what is
 * filled in on the page but is left out of the config. The probe at the bottom tries the saved configuration, so it waits
 * until the page has nothing unsaved.
 */
export function ModelsSection({ form }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.models;
  const queryClient = useQueryClient();
  const probe = useMutation({ mutationFn: runProbe, onSuccess: () => afterCommand(queryClient, PROBE_AFFECTS) });
  const [open, setOpen] = useState<Model | null>(null);
  const dirty = form.changed.size > 0;

  return (
    <SettingsSection section="models" summary={text.summary}>
      <Rows>
        {MODELS.map((model) => (
          <ModelItem
            key={model}
            form={form}
            model={model}
            open={open === model || hasProblems(form, model)}
            onToggle={() => setOpen(open === model ? null : model)}
            result={probe.data?.results.find((result) => result.role === model)}
          />
        ))}
      </Rows>
      {probe.isError ? (
        <p role="alert" className="text-sm text-danger">
          {dictionary.probe.failedTitle}：{errorTextOf(probe.error, dictionary)}
        </p>
      ) : null}
      <CardActions note={dirty ? text.probeDirty : text.probeNote}>
        <Button type="button" variant="outline" disabled={dirty || probe.isPending} onClick={() => probe.mutate()}>
          <BusyIcon busy={probe.isPending} icon={Activity} />
          {dictionary.probe.button}
        </Button>
      </CardActions>
    </SettingsSection>
  );
}

function hasProblems(form: SettingsForm, model: Model): boolean {
  return Object.keys(form.problems.byField).some((path) => path.startsWith(`models.${model}.`));
}

/** One model: the line with its switch, name, what it runs and the edit button, then its settings while open. */
function ModelItem({
  form,
  model,
  open,
  onToggle,
  result,
}: {
  readonly form: SettingsForm;
  readonly model: Model;
  readonly open: boolean;
  readonly onToggle: () => void;
  readonly result: Parameters<typeof ProbeLine>[0]["result"] | undefined;
}) {
  const dictionary = useDictionary();
  const text = dictionary.settings.models;
  const path = `models.${model}`;
  const name = dictionary.dashboard.models[model] ?? model;
  const on = form.drafts[path] === ON;
  const changed = [...form.changed].some((changedPath) => changedPath === path || changedPath.startsWith(`${path}.`));
  const formId = `${inputIdOf(path)}-settings`;
  const urlPath = `${path}.url`;
  const urlExample = text.urlExamples[model][providerOf(form, model)];
  const urlExampleId = `${inputIdOf(urlPath)}-example`;

  return (
    <section aria-labelledby={`${inputIdOf(path)}-name`} className="grid gap-md">
      <div className="flex items-center gap-md">
        <Switch
          id={inputIdOf(path)}
          label={text.enable(name)}
          checked={on}
          changed={form.changed.has(path)}
          onChange={(checked) => form.edit(path, checked ? ON : "")}
        />
        <div className="grid min-w-0 flex-1">
          <span className="flex items-center gap-sm">
            <h3 id={`${inputIdOf(path)}-name`} className="text-sm font-semibold">
              {name}
            </h3>
            {changed ? <span aria-hidden className="size-(--tk-space-sm) rounded-full bg-accent" /> : null}
          </span>
          <Summary form={form} model={model} on={on} />
          {result === undefined ? null : <ProbeLine result={result} />}
        </div>
        <Button type="button" variant="ghost" aria-expanded={open} aria-controls={formId} onClick={onToggle}>
          {open ? text.collapse : text.edit}
          <ChevronDown aria-hidden className={`transition-transform ${open ? "rotate-180" : ""}`} />
        </Button>
      </div>
      {open ? (
        <div id={formId} className="grid gap-md rounded-md bg-canvas p-md">
          {on ? null : <p className="text-xs text-ink-muted">{text.offHint}</p>}
          <Provider form={form} model={model} />
          <Setting
            form={form}
            path={urlPath}
            text={text.fields.url}
            describedBy={urlExample === "" ? undefined : urlExampleId}
            required
            wide
          >
            {urlExample === "" ? null : (
              <p id={urlExampleId} className="text-xs text-ink-muted">
                {text.urlExample}
                <code className="font-mono break-all">{urlExample}</code>
              </p>
            )}
          </Setting>
          <Setting form={form} path={`${path}.model`} text={text.fields.model} required />
          <SecretField form={form} path={`${path}.apiKey`} text={text.fields.apiKey} />
        </div>
      ) : null}
    </section>
  );
}

/** What a model runs, from the inputs as they are now: the model, protocol and address while on, its use while off. */
function Summary({ form, model, on }: { readonly form: SettingsForm; readonly model: Model; readonly on: boolean }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.models;
  if (!on) {
    return <span className="truncate text-xs text-ink-muted">{text.offLine(text.uses[model])}</span>;
  }
  const name = (form.drafts[`models.${model}.model`] ?? "").trim();
  const url = (form.drafts[`models.${model}.url`] ?? "").trim();
  const provider = providerOf(form, model);
  return (
    <span className="truncate text-xs">
      {name === "" ? <span className="text-ink-muted">{text.noModel}</span> : name}
      <span className="text-ink-muted"> · {text.providers[provider]}</span>
      {url === "" ? null : <span className="font-mono text-ink-muted"> · {url}</span>}
    </span>
  );
}

/** The protocol of a model as the inputs have it now. */
function providerOf(form: SettingsForm, model: Model): "generic" | "cloudflare" {
  return (form.drafts[`models.${model}.provider`] ?? "").toLowerCase() === "cloudflare" ? "cloudflare" : "generic";
}

/**
 * The protocol of a model as two segments: the generic protocol (no value) or Cloudflare's, with a line under them on what
 * the one chosen means for this model.
 */
function Provider({ form, model }: { readonly form: SettingsForm; readonly model: Model }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.models;
  const path = `models.${model}.provider`;
  const id = inputIdOf(path);
  const errors = form.problems.byField[path] ?? [];
  const value = providerOf(form, model) === "cloudflare" ? "cloudflare" : "";
  const choices = [
    { value: "", label: text.providers.generic },
    { value: "cloudflare", label: text.providers.cloudflare },
  ];

  return (
    <SettingRow
      name={<SettingName form={form} path={path} text={text.fields.provider} id={`${id}-label`} />}
      control={
        <div
          id={id}
          role="radiogroup"
          tabIndex={-1}
          aria-labelledby={`${id}-label`}
          aria-invalid={errors.length > 0}
          className={`flex w-fit rounded-md border-(length:--tk-border-thin) p-xs outline-none ${form.changed.has(path) ? "border-accent" : "border-line"}`}
        >
          {choices.map((choice) => (
            <button
              key={choice.value}
              type="button"
              role="radio"
              aria-checked={value === choice.value}
              className={`cursor-pointer rounded-sm px-md py-xs text-sm transition-colors ${
                value === choice.value ? "bg-accent text-on-accent" : "text-ink-muted hover:text-ink"
              }`}
              onClick={() => form.edit(path, choice.value)}
            >
              {choice.label}
            </button>
          ))}
        </div>
      }
    >
      <p className="text-xs text-ink-muted">{text.protocols[model][providerOf(form, model)]}</p>
      <Problems id={`${id}-error`} messages={errors} />
    </SettingRow>
  );
}

/** A switch: a button that is on or off, ringed in the accent colour once it differs from the saved state. */
function Switch({
  id,
  label,
  checked,
  changed,
  onChange,
}: {
  readonly id: string;
  readonly label: string;
  readonly checked: boolean;
  readonly changed: boolean;
  readonly onChange: (checked: boolean) => void;
}) {
  return (
    <button
      id={id}
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={label}
      title={label}
      className={`relative inline-flex h-(--tk-space-lg) w-(--tk-space-xl) shrink-0 cursor-pointer items-center rounded-full transition-colors focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent focus-visible:ring-offset-(length:--tk-border-thick) focus-visible:ring-offset-surface ${
        checked ? "bg-accent" : "bg-line"
      } ${changed ? "ring-(length:--tk-border-thick) ring-accent-soft" : ""}`}
      onClick={() => onChange(!checked)}
    >
      <span
        aria-hidden
        className={`inline-block size-(--tk-space-md) rounded-full bg-surface shadow-rest transition-transform ${
          checked
            ? "translate-x-[calc(var(--tk-space-xl)-var(--tk-space-md)-var(--tk-space-xs))]"
            : "translate-x-(--tk-space-xs)"
        }`}
      />
    </button>
  );
}

/**
 * The action bar at the bottom of a settings section that runs commands, under a divider; a short note can stand on the
 * left of the buttons.
 */
export function CardActions({ note, children }: { readonly note?: string; readonly children: ReactNode }) {
  return (
    <footer className="mt-auto flex flex-wrap items-center justify-end gap-sm border-t-(length:--tk-border-thin) border-line pt-md">
      {note === undefined ? null : <span className="mr-auto text-xs text-ink-muted">{note}</span>}
      {children}
    </footer>
  );
}
