import { cn } from "cn";
import { Check } from "lucide-react";
import { useEffect, useState } from "react";
import { useDictionary } from "../../app/session-context";
import { CopyButton } from "../../components/ui/copy-button";
import { copyText } from "../../components/ui/copy-text";
import { useToast } from "../../components/ui/toast-context";
import type { Source } from "./sources";

/** How long a code pill shows its check mark after a copy. */
const COPIED_MILLISECONDS = 1500;

/**
 * A short code in a small monospace pill. A click copies it, so it can be handed to an agent that changes or removes what
 * it names; a copy that worked shows a check mark for a moment and a toast.
 */
export function CodePill({ code, className }: { readonly code: string; readonly className?: string }) {
  const dictionary = useDictionary();
  const showToast = useToast();
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) {
      return;
    }
    const timer = window.setTimeout(() => setCopied(false), COPIED_MILLISECONDS);
    return () => window.clearTimeout(timer);
  }, [copied]);

  const label = dictionary.memory.parts.code(code);
  return (
    <button
      type="button"
      title={dictionary.dashboard.copy(label)}
      aria-label={dictionary.dashboard.copy(label)}
      onClick={async () => {
        if (await copyText(code)) {
          setCopied(true);
          showToast(dictionary.dashboard.copied(label));
        }
      }}
      className={cn(
        "inline-flex shrink-0 cursor-pointer items-center gap-xs rounded-sm bg-surface-raised px-xs font-mono text-xs text-ink-muted outline-none hover:text-ink focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
        copied && "text-success hover:text-success",
        className,
      )}
    >
      {code}
      {copied ? <Check aria-hidden className="size-(--tk-font-size-xs)" /> : null}
    </button>
  );
}

/** Where an entry came from, in small muted type: the day, then the user's words when they were kept. */
export function SourceNote({ source }: { readonly source: Source }) {
  const text = useDictionary().memory.parts;
  return (
    <p className="text-xs text-ink-muted">
      {source.date === null ? null : <span className="tabular-nums">{source.date}</span>}
      {source.date !== null && source.quote !== null ? " · " : null}
      {source.quote === null ? null : text.quote(source.quote)}
    </p>
  );
}

/** A memory file's full path in monospace, cut short with an ellipsis when it does not fit, and a button that copies it. */
export function FilePath({ path }: { readonly path: string }) {
  const dictionary = useDictionary();
  const label = dictionary.memory.parts.path;
  return (
    <div className="flex min-w-0 items-center gap-xs text-xs text-ink-muted">
      <span title={path} className="min-w-0 truncate font-mono select-all">
        {path}
      </span>
      <CopyButton
        value={path}
        label={dictionary.dashboard.copy(label)}
        copiedMessage={dictionary.dashboard.copied(label)}
      />
    </div>
  );
}
