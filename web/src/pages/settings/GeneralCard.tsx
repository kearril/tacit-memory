import { ArrowDown, SlidersHorizontal } from "lucide-react";
import { useDictionary, useSessionStatus } from "../../app/session-context";
import { Card, CardDescription, CardTitle } from "../../components/ui/card";
import { ReadOnlySetting, Setting, type SettingsForm } from "./fields";
import { effective, sectionIdOf, textOf } from "./form";

/**
 * The general card: on the left where the service listens (the port, and the extra listening address with a way to the
 * access card while no access password is set); on the right the memory repository and language, which only
 * `config.yaml` changes, and the limits on briefs and detail pages. On a narrow screen the two columns stack.
 */
export function GeneralCard({ form, className }: { readonly form: SettingsForm; readonly className?: string }) {
  const dictionary = useDictionary();
  const session = useSessionStatus();
  const text = dictionary.settings.general;
  const language = textOf(effective(form.view, "file", "language"));

  return (
    <Card id={sectionIdOf("general")} aria-labelledby="general-title" className={`scroll-mt-xl ${className ?? ""}`}>
      <header className="flex items-start gap-md">
        <span className="grid size-(--tk-size-control) shrink-0 place-items-center rounded-md bg-accent-soft text-accent">
          <SlidersHorizontal aria-hidden className="size-(--tk-font-size-lg)" />
        </span>
        <div className="grid min-w-0 flex-1 gap-xs">
          <CardTitle id="general-title">{text.title}</CardTitle>
          <CardDescription>{text.summary}</CardDescription>
        </div>
      </header>

      <div className="grid gap-lg md:grid-cols-2 md:gap-0">
        <section aria-labelledby="general-service" className="grid content-start gap-md md:pr-lg">
          <h3 id="general-service" className="text-sm font-semibold">
            {text.service}
          </h3>
          <Setting form={form} path="server.port" text={text.fields.port} />
          <Setting form={form} path="server.host" text={text.fields.host} defaultText={text.hostDefault}>
            {session.passwordSet ? null : (
              <button
                type="button"
                className="flex w-fit cursor-pointer items-center gap-xs text-xs font-medium text-accent hover:underline"
                onClick={() => document.getElementById(sectionIdOf("access"))?.scrollIntoView({ behavior: "smooth" })}
              >
                <ArrowDown aria-hidden className="size-(--tk-font-size-sm)" />
                {text.hostNeedsPassword}
              </button>
            )}
          </Setting>
        </section>

        <section
          aria-labelledby="general-memory"
          className="grid content-start gap-md border-t-(length:--tk-border-thin) border-line pt-lg md:border-t-0 md:border-l-(length:--tk-border-thin) md:pt-0 md:pl-lg"
        >
          <h3 id="general-memory" className="text-sm font-semibold">
            {text.memory}
          </h3>
          <ReadOnlySetting
            form={form}
            path="memory.path"
            text={text.fields.memoryPath}
            value={textOf(effective(form.view, "file", "memory.path"))}
          />
          <ReadOnlySetting
            form={form}
            path="language"
            text={text.fields.language}
            value={text.languages[language] ?? language}
          />
          <Setting form={form} path="limits.brief" text={text.fields.brief} />
          <Setting form={form} path="limits.page.minAliases" text={text.fields.minAliases} />
        </section>
      </div>
    </Card>
  );
}
