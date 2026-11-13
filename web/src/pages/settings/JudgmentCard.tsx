import { ScanSearch } from "lucide-react";
import { useDictionary } from "../../app/session-context";
import { Alert, AlertDescription } from "../../components/ui/alert";
import { Card, CardDescription, CardTitle } from "../../components/ui/card";
import { differsFromDefault, inputIdOf, JUDGMENTS, sectionIdOf, valueAt } from "./form";
import {
  DefaultBar,
  Explaining,
  NumberSetting,
  Problems,
  RestoreButton,
  type SettingsForm,
  SettingInput,
} from "./fields";

/**
 * The retrieval and judgment card: the calibration note across the top, then the three retrieval settings on the left
 * and the four pairs of judgment thresholds on the right, as a small table with the upper and the lower threshold side
 * by side. On a narrow screen the two columns stack.
 */
export function JudgmentCard({ form, className }: { readonly form: SettingsForm; readonly className?: string }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.judgment;

  return (
    <Card id={sectionIdOf("judgment")} aria-labelledby="judgment-title" className={`scroll-mt-xl ${className ?? ""}`}>
      <header className="flex items-start gap-md">
        <span className="grid size-(--tk-size-control) shrink-0 place-items-center rounded-md bg-accent-soft text-accent">
          <ScanSearch aria-hidden className="size-(--tk-font-size-lg)" />
        </span>
        <div className="grid min-w-0 flex-1 gap-xs">
          <CardTitle id="judgment-title">{text.title}</CardTitle>
          <CardDescription>{text.summary}</CardDescription>
        </div>
      </header>

      <Alert tone="info">
        <AlertDescription>{text.calibration}</AlertDescription>
      </Alert>

      <div className="grid gap-lg md:grid-cols-2 md:gap-0">
        <section aria-labelledby="judgment-retrieval" className="grid content-start gap-md md:pr-lg">
          <h3 id="judgment-retrieval" className="text-sm font-semibold">
            {text.retrieval}
          </h3>
          <NumberSetting form={form} path="retrieval.candidates" text={text.fields.candidates} />
          <NumberSetting form={form} path="retrieval.topK" text={text.fields.topK} />
          <NumberSetting form={form} path="retrieval.minRerankScore" text={text.fields.minRerankScore} />
        </section>

        <section
          aria-labelledby="judgment-thresholds"
          className="grid content-start gap-md border-t-(length:--tk-border-thin) border-line pt-lg md:border-t-0 md:border-l-(length:--tk-border-thin) md:pt-0 md:pl-lg"
        >
          <div className="grid gap-xs">
            <h3 id="judgment-thresholds" className="text-sm font-semibold">
              {text.thresholds}
            </h3>
            <p className="text-xs text-ink-muted">
              {text.thresholdsHelp} {dictionary.settings.range("0", "1")}
            </p>
          </div>
          <ThresholdTable form={form} />
        </section>
      </div>
    </Card>
  );
}

/** The four pairs of thresholds: one row each, with the column heads above the inputs. */
function ThresholdTable({ form }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.judgment;
  const columns = "grid grid-cols-[minmax(0,1fr)_minmax(0,6rem)_minmax(0,6rem)_auto] items-center gap-x-xs";

  return (
    <div role="table" aria-labelledby="judgment-thresholds" className="grid gap-sm">
      <div role="row" className={`${columns} pl-md text-xs text-ink-muted`}>
        <span role="columnheader">
          <span className="sr-only">{text.thresholds}</span>
        </span>
        <span role="columnheader">{text.certain}</span>
        <span role="columnheader">{text.unlikely}</span>
        <span aria-hidden className="w-(--tk-size-control)" />
      </div>
      {JUDGMENTS.map((judgment) => {
        const group = `decision.${judgment}`;
        const explained = text.judgments[judgment];
        const differs = differsFromDefault(form.view, form.drafts, group);
        const errors = [group, `${group}.certain`, `${group}.unlikely`].flatMap((path) => form.problems.byField[path] ?? []);
        const errorId = `${inputIdOf(group)}-error`;
        const defaults = [valueAt(form.view.defaults, `${group}.certain`), valueAt(form.view.defaults, `${group}.unlikely`)];
        return (
          <div key={judgment} role="row" className="relative grid gap-xs pl-md">
            <DefaultBar shown={differs} />
            <div className={columns}>
              <div role="rowheader" className="grid min-w-0">
                <Explaining help={explained.help}>
                  <span className="truncate text-sm font-medium">{explained.label}</span>
                </Explaining>
                <span className="truncate text-xs text-ink-muted">
                  {dictionary.settings.defaultValue(defaults.map(String).join(" / "))}
                </span>
              </div>
              {(["certain", "unlikely"] as const).map((bound) => {
                const path = `${group}.${bound}`;
                return (
                  <div key={bound} role="cell">
                    <SettingInput
                      form={form}
                      path={path}
                      id={`setting-${path.replaceAll(".", "-")}`}
                      label={`${explained.label} ${text[bound]}`}
                      help={explained.help}
                      errors={errors}
                      errorId={errorId}
                    />
                  </div>
                );
              })}
              <div role="cell">
                <RestoreButton
                  shown={differs}
                  label={`${dictionary.settings.restoreDefault}: ${explained.label}`}
                  onClick={() => form.restore(group)}
                />
              </div>
            </div>
            <Problems id={errorId} messages={errors} />
          </div>
        );
      })}
    </div>
  );
}
