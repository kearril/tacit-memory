import * as React from "react";
import { cn } from "cn";
import { DropdownMenu as DropdownMenuPrimitive } from "radix-ui";

// Structure and behaviour follow shadcn/ui's dropdown menu: a list of actions that opens from a button and is used with the
// keyboard as well as the pointer. It floats above everything, dialogs included, at the layer of the tooltips.

function DropdownMenu({ ...props }: React.ComponentProps<typeof DropdownMenuPrimitive.Root>) {
  return <DropdownMenuPrimitive.Root data-slot="dropdown-menu" {...props} />;
}

function DropdownMenuTrigger({ ...props }: React.ComponentProps<typeof DropdownMenuPrimitive.Trigger>) {
  return <DropdownMenuPrimitive.Trigger data-slot="dropdown-menu-trigger" {...props} />;
}

function DropdownMenuContent({
  className,
  sideOffset = 4,
  align = "end",
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Content>) {
  return (
    <DropdownMenuPrimitive.Portal>
      <DropdownMenuPrimitive.Content
        data-slot="dropdown-menu-content"
        sideOffset={sideOffset}
        align={align}
        className={cn(
          "z-(--tk-layer-toast) grid min-w-(--radix-dropdown-menu-trigger-width) gap-xs rounded-md border-(length:--tk-border-thin) border-line bg-surface p-xs text-ink shadow-raised",
          className,
        )}
        {...props}
      />
    </DropdownMenuPrimitive.Portal>
  );
}

/** One action of the menu. A disabled item stays in the list, dimmed, with `hint` saying why it cannot be chosen. */
function DropdownMenuItem({
  className,
  hint,
  children,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Item> & { hint?: string }) {
  return (
    <DropdownMenuPrimitive.Item
      data-slot="dropdown-menu-item"
      className={cn(
        "flex cursor-pointer items-center gap-sm rounded-sm px-sm py-xs text-sm outline-none select-none data-highlighted:bg-surface-raised data-disabled:cursor-not-allowed data-disabled:opacity-(--tk-opacity-disabled) [&_svg]:size-(--tk-font-size-sm) [&_svg]:shrink-0",
        className,
      )}
      {...props}
    >
      {children}
      {hint === undefined ? null : <span className="ml-auto pl-md text-xs text-ink-muted">{hint}</span>}
    </DropdownMenuPrimitive.Item>
  );
}

export { DropdownMenu, DropdownMenuContent, DropdownMenuItem, DropdownMenuTrigger };
