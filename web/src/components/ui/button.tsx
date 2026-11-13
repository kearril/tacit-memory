import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "cn";
import { Slot } from "radix-ui";

// Structure and behaviour follow shadcn/ui. The colours, sizes and motion are the project's tokens (see styles/tokens.css).
const buttonVariants = cva(
  "inline-flex h-control shrink-0 cursor-pointer items-center justify-center gap-sm rounded-md px-md text-sm font-medium whitespace-nowrap transition-colors outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent disabled:pointer-events-none disabled:opacity-(--tk-opacity-disabled)",
  {
    variants: {
      variant: {
        default: "bg-accent text-on-accent hover:bg-accent/(--tk-mix-hover)",
        destructive: "bg-danger text-on-accent hover:bg-danger/(--tk-mix-hover) focus-visible:ring-danger",
        outline: "border-(length:--tk-border-thin) border-line bg-surface text-ink hover:bg-surface-raised",
        ghost: "text-ink hover:bg-surface-raised",
      },
    },
    defaultVariants: {
      variant: "default",
    },
  },
);

function Button({
  className,
  variant = "default",
  asChild = false,
  ...props
}: React.ComponentProps<"button"> &
  VariantProps<typeof buttonVariants> & {
    asChild?: boolean;
  }) {
  const Comp = asChild ? Slot.Root : "button";

  return (
    <Comp
      data-slot="button"
      data-variant={variant}
      className={cn(buttonVariants({ variant, className }))}
      {...props}
    />
  );
}

export { Button };
