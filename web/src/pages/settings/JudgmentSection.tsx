import { useDictionary } from "../../app/session-context";
import { differsFromDefault, inputIdOf, JUDGMENTS, outOfRange, textOf, valueAt } from "./form";
import {
  Explaining,
  Problems,
  RestoreButton,
  Setting,
  SettingInput,
  SettingsGroup,
  type SettingsForm,
  SettingsSection,
} from "./fields";

/**
 * The retrieval and judgment section: the three retrieval settings, then the four pairs of judgment thresholds as a small
 * table with the upper and the lower threshold side by side.
 */
export function JudgmentSection({ form }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.judgment;

  return (
    <SettingsSection section="judgment" summary={text.summary}>
      <SettingsGroup title={text.retrieval}>
        <Setting form={form} path="retrieval.candidates" text={text.fields.candidates} />
        <Setting form={form} path="retrieval.topK" text={text.fields.topK} />
        <Setting form={form} path="retrieval.minRerankScore" text={text.fields.minRerankScore} />
      </SettingsGroup>
      <SettingsGroup title={text.thresholds} note={`${text.thresholdsHelp} ${text.calibration}`}>
        <ThresholdTable form={form} />
      </SettingsGroup>
    </SettingsSection>
  );
}

const BOUNDS = ["certain", "unlikely"] as const;

/**
 * The four pairs of thresholds: one row each under the column heads. The heads give the defaults when every pair has the
 * same; a pair that differs from its default has the button that restores it.
 */
function ThresholdTable({ form }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.judgment;
  // The names line up with the names of the rows above; the two inputs sit close, as one pair.
  const columns = "grid grid-cols-[minmax(0,var(--tk-size-setting-name))_minmax(0,1fr)] items-center gap-x-lg";
  // Two inputs as wide together as one number input above, so the pair lines up with the rows.
  const pair =
    "grid grid-cols-[minmax(0,calc((var(--tk-size-tile)-var(--tk-space-sm))/2))_minmax(0,calc((var(--tk-size-tile)-var(--tk-space-sm))/2))_minmax(0,1fr)] items-center gap-x-sm";
  const shared = BOUNDS.map((bound) => {
    const values = new Set(
      JUDGMENTS.map((judgment) => textOf(valueAt(form.view.defaults, `decision.${judgment}.${bound}`))),
    );
    return values.size === 1 ? [...values][0] : null;
  });

  return (
    <div role="table" aria-label={text.thresholds} className="grid gap-sm">
      <div role="row" className={`${columns} text-xs text-ink-muted`}>
        <span role="columnheader">
          <span className="sr-only">{text.thresholds}</span>
        </span>
        <div className={pair}>
          {BOUNDS.map((bound, index) => (
            <span key={bound} role="columnheader" className="grid">
              <span className="font-medium text-ink">{text[bound]}</span>
              {shared[index] === null ? null : <span>{dictionary.settings.defaultValue(shared[index])}</span>}
            </span>
          ))}
          <span aria-hidden />
        </div>
      </div>
      {JUDGMENTS.map((judgment) => {
        const group = `decision.${judgment}`;
        const explained = text.judgments[judgment];
        const differs = differsFromDefault(form.view, form.drafts, group);
        const ranges = BOUNDS.flatMap((bound) => {
          const range = outOfRange(form.view, form.drafts, `${group}.${bound}`);
          return range === null
            ? []
            : [`${text[bound]} ${dictionary.settings.outOfRange(String(range.min), String(range.max))}`];
        });
        const errors = [
          ...ranges,
          ...[group, `${group}.certain`, `${group}.unlikely`].flatMap((path) => form.problems.byField[path] ?? []),
        ];
        const errorId = `${inputIdOf(group)}-error`;
        return (
          <div key={judgment} role="row" className="grid gap-xs">
            <div className={columns}>
              <div role="rowheader" className="min-w-0">
                <Explaining help={explained.help} keyPath={group}>
                  <span
                    tabIndex={0}
                    className="truncate rounded-xs text-sm font-medium outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
                  >
                    {explained.label}
                  </span>
                </Explaining>
              </div>
              <div className={pair}>
                {BOUNDS.map((bound) => {
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
                  {differs ? (
                    <RestoreButton
                      label={`${dictionary.settings.restoreDefault}: ${explained.label}`}
                      value={
                        shared.every((value) => value !== null)
                          ? null
                          : BOUNDS.map((bound) => textOf(valueAt(form.view.defaults, `${group}.${bound}`))).join(" / ")
                      }
                      onClick={() => form.restore(group)}
                    />
                  ) : null}
                </div>
              </div>
            </div>
            <Problems id={errorId} messages={errors} />
          </div>
        );
      })}
    </div>
  );
}
