import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "cn";
import { Slot } from "radix-ui";

// Structure and behaviour follow shadcn/ui. The colours, sizes and motion are the project's tokens (see styles/tokens.css).
const buttonVariants = cva(
  "inline-flex h-control shrink-0 lift cursor-pointer items-center justify-center gap-sm rounded-md text-sm font-medium whitespace-nowrap outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent focus-visible:ring-offset-(length:--tk-border-thick) focus-visible:ring-offset-surface disabled:pointer-events-none disabled:opacity-(--tk-opacity-disabled) aria-disabled:cursor-not-allowed aria-disabled:opacity-(--tk-opacity-disabled) [&_svg]:size-(--tk-font-size-base) [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        default: "bg-accent text-on-accent shadow-pressed hover:bg-accent/(--tk-mix-hover) hover:shadow-hover",
        destructive:
          "bg-danger text-on-accent shadow-pressed hover:bg-danger/(--tk-mix-hover) focus-visible:ring-danger",
        outline:
          "border-(length:--tk-border-thin) border-line bg-surface text-ink hover:bg-surface-raised hover:shadow-pressed",
        ghost: "text-ink hover:bg-surface-raised",
      },
      size: {
        default: "px-md",
        icon: "w-(--tk-size-control)",
      },
    },
    defaultVariants: {
      variant: "default",
      size: "default",
    },
  },
);

function Button({
  className,
  variant = "default",
  size = "default",
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
      className={cn(buttonVariants({ variant, size, className }))}
      {...props}
    />
  );
}

export { Button };
