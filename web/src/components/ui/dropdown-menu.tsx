import * as React from "react";
import { Check } from "lucide-react";
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

const itemClass =
  "flex cursor-pointer items-center gap-sm rounded-sm px-sm py-xs text-sm outline-none select-none data-disabled:cursor-not-allowed data-disabled:opacity-(--tk-opacity-disabled) data-highlighted:bg-surface-raised [&_svg]:size-(--tk-font-size-sm) [&_svg]:shrink-0";

/** One action of the menu. A disabled item stays in the list, dimmed, with `hint` saying why it cannot be chosen. */
function DropdownMenuItem({
  className,
  hint,
  children,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.Item> & { hint?: string }) {
  return (
    <DropdownMenuPrimitive.Item data-slot="dropdown-menu-item" className={cn(itemClass, className)} {...props}>
      {children}
      {hint === undefined ? null : <span className="ml-auto pl-md text-xs text-ink-muted">{hint}</span>}
    </DropdownMenuPrimitive.Item>
  );
}

/** A small heading over a group of items, in secondary text. */
function DropdownMenuLabel({ className, ...props }: React.ComponentProps<typeof DropdownMenuPrimitive.Label>) {
  return (
    <DropdownMenuPrimitive.Label
      data-slot="dropdown-menu-label"
      className={cn("px-sm pt-xs text-xs text-ink-muted", className)}
      {...props}
    />
  );
}

function DropdownMenuSeparator({ className, ...props }: React.ComponentProps<typeof DropdownMenuPrimitive.Separator>) {
  return (
    <DropdownMenuPrimitive.Separator
      data-slot="dropdown-menu-separator"
      className={cn("my-xs h-(--tk-border-thin) bg-line", className)}
      {...props}
    />
  );
}

function DropdownMenuRadioGroup({ ...props }: React.ComponentProps<typeof DropdownMenuPrimitive.RadioGroup>) {
  return <DropdownMenuPrimitive.RadioGroup data-slot="dropdown-menu-radio-group" {...props} />;
}

/** One choice of a radio group; the chosen one ends with a check in the accent colour. */
function DropdownMenuRadioItem({
  className,
  children,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.RadioItem>) {
  return (
    <DropdownMenuPrimitive.RadioItem
      data-slot="dropdown-menu-radio-item"
      className={cn(itemClass, className)}
      {...props}
    >
      {children}
      <DropdownMenuPrimitive.ItemIndicator className="ml-auto pl-md text-accent">
        <Check aria-hidden />
      </DropdownMenuPrimitive.ItemIndicator>
    </DropdownMenuPrimitive.RadioItem>
  );
}

/** An option that is on or off; when on it ends with a check in the accent colour. */
function DropdownMenuCheckboxItem({
  className,
  children,
  ...props
}: React.ComponentProps<typeof DropdownMenuPrimitive.CheckboxItem>) {
  return (
    <DropdownMenuPrimitive.CheckboxItem
      data-slot="dropdown-menu-checkbox-item"
      className={cn(itemClass, className)}
      {...props}
    >
      {children}
      <DropdownMenuPrimitive.ItemIndicator className="ml-auto pl-md text-accent">
        <Check aria-hidden />
      </DropdownMenuPrimitive.ItemIndicator>
    </DropdownMenuPrimitive.CheckboxItem>
  );
}

export {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
};
