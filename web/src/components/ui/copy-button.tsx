import * as React from "react";
import { cn } from "cn";
import { Check, Copy } from "lucide-react";
import { useToast } from "./toast-context";

/** How long the button shows its check mark after a copy. */
const COPIED_MILLISECONDS = 1500;

/**
 * Puts text on the clipboard. The clipboard interface exists only in a secure context, which a page opened from another
 * device over plain HTTP is not; there the text is copied through a hidden text field instead.
 */
async function copyText(text: string): Promise<boolean> {
  if (window.isSecureContext && navigator.clipboard !== undefined) {
    try {
      await navigator.clipboard.writeText(text);
      return true;
    } catch {
      // Fall through to the text field.
    }
  }
  const field = document.createElement("textarea");
  field.value = text;
  field.setAttribute("readonly", "");
  field.style.position = "fixed";
  field.style.opacity = "0";
  document.body.append(field);
  field.select();
  try {
    return document.execCommand("copy");
  } finally {
    field.remove();
  }
}

/**
 * A small icon button that copies a value. A copy that worked turns the icon into a check mark for a moment and shows a
 * toast; one that did not changes nothing, and the value stays on the page to select by hand.
 */
function CopyButton({
  value,
  label,
  copiedMessage,
  className,
}: {
  value: string;
  label: string;
  copiedMessage: string;
  className?: string;
}) {
  const showToast = useToast();
  const [copied, setCopied] = React.useState(false);

  React.useEffect(() => {
    if (!copied) {
      return;
    }
    const timer = window.setTimeout(() => setCopied(false), COPIED_MILLISECONDS);
    return () => window.clearTimeout(timer);
  }, [copied]);

  const Icon = copied ? Check : Copy;
  return (
    <button
      type="button"
      aria-label={label}
      title={label}
      onClick={async () => {
        if (await copyText(value)) {
          setCopied(true);
          showToast(copiedMessage);
        }
      }}
      className={cn(
        "grid size-(--tk-size-icon-tile) shrink-0 cursor-pointer place-items-center rounded-sm text-ink-muted outline-none hover:bg-surface-raised hover:text-ink focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
        copied && "text-success hover:text-success",
        className,
      )}
    >
      <Icon aria-hidden className="size-(--tk-font-size-sm)" />
    </button>
  );
}

export { CopyButton };
