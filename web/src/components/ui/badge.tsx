import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "cn";

// A short state label on a soft wash of its colour. A badge that carries a state also carries an icon or words that say
// it, so the state never rests on colour alone.
const badgeVariants = cva(
  "inline-flex shrink-0 items-center gap-xs rounded-full px-sm py-xs text-xs leading-(--tk-line-height-tight) font-medium whitespace-nowrap [&_svg]:size-(--tk-font-size-sm) [&_svg]:shrink-0",
  {
    variants: {
      tone: {
        neutral: "bg-surface-raised text-ink-muted",
        accent: "bg-accent-soft text-accent",
        success: "bg-success-soft text-success",
        warning: "bg-warning-soft text-warning",
        danger: "bg-danger-soft text-danger",
        info: "bg-info-soft text-info",
      },
    },
    defaultVariants: {
      tone: "neutral",
    },
  },
);

function Badge({ className, tone, ...props }: React.ComponentProps<"span"> & VariantProps<typeof badgeVariants>) {
  return <span data-slot="badge" className={cn(badgeVariants({ tone, className }))} {...props} />;
}

export { Badge };
