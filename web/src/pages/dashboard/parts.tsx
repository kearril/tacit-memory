import type { ComponentProps, ReactNode } from "react";
import type { ProbeResult } from "../../api/probe";
import { useDictionary } from "../../app/session-context";
import { Button } from "../../components/ui/button";
import { Tooltip, TooltipContent, TooltipTrigger } from "../../components/ui/tooltip";

/**
 * One line of a two-column list (a `dl` laid out as `auto` and the rest): its label on the left, its value on the right
 * with what goes after the value. A long value truncates in a span of its own.
 */
export function InfoRow({ label, children }: { readonly label: string; readonly children: ReactNode }) {
  return (
    <div className="contents">
      <dt className="py-xs text-ink-muted">{label}</dt>
      <dd className="flex min-w-0 items-center gap-xs">{children}</dd>
    </div>
  );
}

/**
 * A command in the head of a dashboard card: a square button with only its icon, whose name shows in a tooltip on hover
 * or keyboard focus and is read out as the button's name. It takes the props of a button, so a dialog's or a menu's
 * trigger can wrap it, and `asChild` for a link. A dialog or menu it opened hands the focus back when it closes; after a
 * click that focus is not a keyboard one, so the tooltip stays shut rather than popping up on its own.
 */
export function IconAction({
  label,
  variant = "outline",
  size = "icon",
  ...props
}: ComponentProps<typeof Button> & { readonly label: string }) {
  return (
    <Tooltip>
      <TooltipTrigger
        asChild
        onFocus={(event) => {
          if (!event.currentTarget.matches(":focus-visible")) {
            event.preventDefault();
          }
        }}
      >
        <Button variant={variant} size={size} aria-label={label} {...props} />
      </TooltipTrigger>
      <TooltipContent>{label}</TooltipContent>
    </Tooltip>
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

/** What the last probe of one model found, on a small line under the model: success, a skip, or how it failed. */
export function ProbeLine({ result }: { readonly result: ProbeResult }) {
  const dictionary = useDictionary();
  const text = dictionary.dashboard.probe;
  const milliseconds = result.elapsedMilliseconds ?? 0;
  if (result.outcome === "succeeded") {
    return <span className="text-xs text-success">{text.succeeded(milliseconds)}</span>;
  }
  if (result.outcome === "unconfigured") {
    return <span className="text-xs text-ink-muted">{text.skipped}</span>;
  }
  const reason =
    result.failure === "httpStatus" && result.statusCode !== null
      ? text.httpStatus(result.statusCode)
      : result.failure === "wrongFormat"
        ? text.wrongFormat
        : result.failure === "timeout"
          ? text.timeout
          : text.network;
  return (
    <span title={result.responseBody ?? undefined} className="truncate text-xs text-danger">
      {text.failed(reason, milliseconds)}
    </span>
  );
}
