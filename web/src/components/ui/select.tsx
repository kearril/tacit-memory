import { cn } from "cn";
import { Check, ChevronDown } from "lucide-react";
import { Select as SelectPrimitive } from "radix-ui";
import { FIELD_TRIGGER } from "./field-trigger";

// Structure and behaviour follow shadcn/ui's select: a button in the look of the inputs that opens a list in the look of
// the dropdown menu, used with the keyboard as well as the pointer. It takes the choices as a list; an empty value, which
// the primitive cannot hold, stands for "no choice" (such as "all"), so callers keep their empty strings.

/** The value the primitive holds for an empty choice. */
const EMPTY = "\u0000";

interface SelectOption {
  readonly value: string;
  readonly label: string;
}

function Select({
  id,
  value,
  onValueChange,
  options,
  className,
  ...props
}: {
  readonly id?: string;
  readonly value: string;
  readonly onValueChange: (value: string) => void;
  readonly options: readonly SelectOption[];
  readonly className?: string;
  readonly "aria-invalid"?: boolean;
  readonly "aria-label"?: string;
}) {
  return (
    <SelectPrimitive.Root
      value={value === "" ? EMPTY : value}
      onValueChange={(next) => onValueChange(next === EMPTY ? "" : next)}
    >
      <SelectPrimitive.Trigger
        id={id}
        data-slot="select-trigger"
        className={cn(FIELD_TRIGGER, "justify-between", className)}
        {...props}
      >
        <span className="min-w-0 truncate">
          <SelectPrimitive.Value />
        </span>
        <SelectPrimitive.Icon asChild>
          <ChevronDown aria-hidden className="size-(--tk-font-size-base) shrink-0 text-ink-muted" />
        </SelectPrimitive.Icon>
      </SelectPrimitive.Trigger>
      <SelectPrimitive.Portal>
        <SelectPrimitive.Content
          data-slot="select-content"
          position="popper"
          sideOffset={4}
          className="z-(--tk-layer-toast) max-h-(--radix-select-content-available-height) min-w-(--radix-select-trigger-width) overflow-hidden rounded-md border-(length:--tk-border-thin) border-line bg-surface text-ink shadow-raised"
        >
          <SelectPrimitive.Viewport className="grid gap-xs p-xs">
            {options.map((option) => (
              <SelectPrimitive.Item
                key={option.value}
                value={option.value === "" ? EMPTY : option.value}
                className="relative flex cursor-pointer items-center gap-sm rounded-sm py-xs pr-sm pl-lg text-sm outline-none select-none data-highlighted:bg-surface-raised data-[state=checked]:font-medium data-[state=checked]:text-accent"
              >
                <SelectPrimitive.ItemIndicator className="absolute left-xs inline-flex">
                  <Check aria-hidden className="size-(--tk-font-size-sm)" />
                </SelectPrimitive.ItemIndicator>
                <SelectPrimitive.ItemText>{option.label}</SelectPrimitive.ItemText>
              </SelectPrimitive.Item>
            ))}
          </SelectPrimitive.Viewport>
        </SelectPrimitive.Content>
      </SelectPrimitive.Portal>
    </SelectPrimitive.Root>
  );
}

export { Select, type SelectOption };
