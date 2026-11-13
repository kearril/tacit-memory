import { Sparkles } from "lucide-react";
import { useDictionary } from "../../app/session-context";
import { Card, CardDescription, CardTitle } from "../../components/ui/card";
import { Setting, type SettingsForm } from "./fields";
import { CONSOLIDATION, sectionIdOf } from "./form";

/** The consolidation card: its six whole numbers, one under another. */
export function ConsolidationCard({ form, className }: { readonly form: SettingsForm; readonly className?: string }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.consolidation;

  return (
    <Card id={sectionIdOf("consolidation")} aria-labelledby="consolidation-title" className={`scroll-mt-xl ${className ?? ""}`}>
      <header className="flex items-start gap-md">
        <span className="grid size-(--tk-size-control) shrink-0 place-items-center rounded-md bg-accent-soft text-accent">
          <Sparkles aria-hidden className="size-(--tk-font-size-lg)" />
        </span>
        <div className="grid min-w-0 flex-1 gap-xs">
          <CardTitle id="consolidation-title">{text.title}</CardTitle>
          <CardDescription>{text.summary}</CardDescription>
        </div>
      </header>
      <div className="grid gap-md">
        {CONSOLIDATION.map((key) => (
          <Setting key={key} form={form} path={`consolidation.${key}`} text={text.fields[key]} />
        ))}
      </div>
    </Card>
  );
}
