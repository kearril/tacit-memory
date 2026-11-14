import * as React from "react";
import { cn } from "cn";
import { Popover as PopoverPrimitive } from "radix-ui";

// Structure and behaviour follow shadcn/ui's popover: a panel that opens on a click of its trigger, so a pointer without
// hover (a touch screen) and the keyboard reach it as well, and closes on a click outside or Escape. For explanations
// longer than a tooltip carries.

function Popover({ ...props }: React.ComponentProps<typeof PopoverPrimitive.Root>) {
  return <PopoverPrimitive.Root data-slot="popover" {...props} />;
}

function PopoverTrigger({ ...props }: React.ComponentProps<typeof PopoverPrimitive.Trigger>) {
  return <PopoverPrimitive.Trigger data-slot="popover-trigger" {...props} />;
}

function PopoverContent({
  className,
  align = "start",
  sideOffset = 8,
  ...props
}: React.ComponentProps<typeof PopoverPrimitive.Content>) {
  return (
    <PopoverPrimitive.Portal>
      <PopoverPrimitive.Content
        data-slot="popover-content"
        align={align}
        sideOffset={sideOffset}
        className={cn(
          "z-(--tk-layer-toast) grid max-w-content gap-sm rounded-md border-(length:--tk-border-thin) border-line bg-surface p-md text-sm leading-(--tk-line-height-normal) text-ink shadow-raised outline-none",
          className,
        )}
        {...props}
      />
    </PopoverPrimitive.Portal>
  );
}

export { Popover, PopoverContent, PopoverTrigger };
