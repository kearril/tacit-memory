import type { ComponentProps, ReactNode } from "react";
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
