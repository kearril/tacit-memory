import { useMutation } from "@tanstack/react-query";
import { Activity, Cpu } from "lucide-react";
import { runProbe } from "../../api/probe";
import { useDictionary } from "../../app/session-context";
import { Button } from "../../components/ui/button";
import { Card, CardDescription, CardTitle } from "../../components/ui/card";
import { errorTextOf } from "../../i18n/messages";
import { BusyIcon, CardActions, ProbeLine } from "../dashboard/parts";
import { Explaining, Problems, RestartBadge, Setting, type SettingsForm } from "./fields";
import { inputIdOf, MODELS, ON, sectionIdOf } from "./form";
import { SecretField } from "./SecretField";

/**
 * The models card: the three auxiliary models side by side, each with a switch that enables it, its protocol, address,
 * model name and API key. A model switched off keeps what is filled in on the page but is left out of the config. The
 * probe at the bottom tries the saved configuration, so it waits until the page has nothing unsaved.
 */
export function ModelsCard({ form, className }: { readonly form: SettingsForm; readonly className?: string }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.models;
  const probe = useMutation({ mutationFn: runProbe });
  const dirty = form.changed.size > 0;

  return (
    <Card id={sectionIdOf("models")} aria-labelledby="models-title" className={`scroll-mt-xl ${className ?? ""}`}>
      <header className="flex items-start gap-md">
        <span className="grid size-(--tk-size-control) shrink-0 place-items-center rounded-md bg-accent-soft text-accent">
          <Cpu aria-hidden className="size-(--tk-font-size-lg)" />
        </span>
        <div className="grid min-w-0 flex-1 gap-xs">
          <CardTitle id="models-title">{text.title}</CardTitle>
          <CardDescription>{text.summary}</CardDescription>
        </div>
      </header>

      <div className="grid gap-lg lg:grid-cols-3">
        {MODELS.map((model) => (
          <Model key={model} form={form} model={model} result={probe.data?.results.find((result) => result.role === model)} />
        ))}
      </div>

      {probe.isError ? (
        <p role="alert" className="text-sm text-danger">
          {dictionary.dashboard.probe.failedTitle}：{errorTextOf(probe.error, dictionary)}
        </p>
      ) : null}

      <CardActions note={dirty ? text.probeDirty : dictionary.dashboard.probe.note}>
        <Button type="button" variant="outline" disabled={dirty || probe.isPending} onClick={() => probe.mutate()}>
          <BusyIcon busy={probe.isPending} icon={Activity} />
          {dictionary.dashboard.probe.button}
        </Button>
      </CardActions>
    </Card>
  );
}

/** One model's column: its name and switch, then its settings, dimmed and disabled while it is switched off. */
function Model({
  form,
  model,
  result,
}: {
  readonly form: SettingsForm;
  readonly model: (typeof MODELS)[number];
  readonly result: Parameters<typeof ProbeLine>[0]["result"] | undefined;
}) {
  const dictionary = useDictionary();
  const text = dictionary.settings.models;
  const path = `models.${model}`;
  const name = dictionary.dashboard.models[model] ?? model;
  const on = form.drafts[path] === ON;
  const changed = form.changed.has(path);

  return (
    <section aria-labelledby={`${inputIdOf(path)}-name`} className="grid content-start gap-md rounded-lg bg-surface-raised p-md">
      <header className="flex items-center justify-between gap-md">
        <h3 id={`${inputIdOf(path)}-name`} className="text-sm font-semibold">
          {name}
        </h3>
        <Switch
          id={inputIdOf(path)}
          label={text.enable(name)}
          checked={on}
          changed={changed}
          onChange={(checked) => form.edit(path, checked ? ON : "")}
        />
      </header>
      {on ? null : <p className="text-xs text-ink-muted">{text.off}</p>}
      <fieldset disabled={!on} className={`grid min-w-0 gap-md ${on ? "" : "opacity-50"}`}>
        <Provider form={form} path={`${path}.provider`} />
        <Setting form={form} path={`${path}.url`} text={text.fields.url} required wide />
        <Setting form={form} path={`${path}.model`} text={text.fields.model} required wide />
        <SecretField form={form} path={`${path}.apiKey`} text={text.fields.apiKey} />
      </fieldset>
      {result === undefined ? null : <ProbeLine result={result} />}
    </section>
  );
}

/** The protocol of a model as two segments: the generic protocol (no value) or Cloudflare's. */
function Provider({ form, path }: { readonly form: SettingsForm; readonly path: string }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.models;
  const id = inputIdOf(path);
  const errors = form.problems.byField[path] ?? [];
  const value = (form.drafts[path] ?? "").toLowerCase() === "cloudflare" ? "cloudflare" : "";
  const choices = [
    { value: "", label: text.providers.generic },
    { value: "cloudflare", label: text.providers.cloudflare },
  ];

  return (
    <div className="grid gap-xs pl-md">
      <div className="flex min-w-0 flex-wrap items-baseline gap-x-sm">
        <Explaining help={text.fields.provider.help}>
          <span id={`${id}-label`} className="text-sm font-medium">
            {text.fields.provider.label}
          </span>
        </Explaining>
        <code className="min-w-0 font-mono text-xs break-all text-ink-muted">{path}</code>
        <RestartBadge form={form} path={path} />
      </div>
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
            className={`rounded-sm px-md py-xs text-sm transition-colors disabled:cursor-not-allowed ${
              value === choice.value ? "bg-accent text-on-accent" : "text-ink-muted hover:text-ink"
            }`}
            onClick={() => form.edit(path, choice.value)}
          >
            {choice.label}
          </button>
        ))}
      </div>
      <Problems id={`${id}-error`} messages={errors} />
    </div>
  );
}

/** A switch: a button that is on or off, outlined in the accent colour once it differs from the saved state. */
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
      className={`relative inline-flex h-(--tk-space-lg) w-(--tk-space-xl) shrink-0 items-center rounded-full transition-colors focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-accent ${
        checked ? "bg-accent" : "bg-line"
      } ${changed ? "ring-2 ring-accent/40" : ""}`}
      onClick={() => onChange(!checked)}
    >
      <span
        aria-hidden
        className={`inline-block size-(--tk-space-md) rounded-full bg-surface shadow-sm transition-transform ${
          checked ? "translate-x-[calc(var(--tk-space-xl)-var(--tk-space-md)-var(--tk-space-xs))]" : "translate-x-(--tk-space-xs)"
        }`}
      />
    </button>
  );
}
