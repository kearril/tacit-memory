import { cva } from "class-variance-authority";
import type { LucideIcon } from "lucide-react";
import { cn } from "cn";

// An icon on a soft tile of its tone, ahead of a card's or a section's title. The compact tile heads cards and settings
// sections; the large one, a control high, heads the dashboard's overview.
const tileVariants = cva("grid shrink-0 place-items-center", {
  variants: {
    tone: {
      accent: "bg-accent-soft text-accent",
      success: "bg-success-soft text-success",
      warning: "bg-warning-soft text-warning",
      danger: "bg-danger-soft text-danger",
    },
    size: {
      compact: "size-(--tk-size-icon-tile) rounded-sm [&_svg]:size-(--tk-font-size-sm)",
      large: "size-(--tk-size-control) rounded-md [&_svg]:size-(--tk-font-size-lg)",
    },
  },
  defaultVariants: {
    tone: "accent",
    size: "compact",
  },
});

function IconTile({
  icon: Icon,
  tone,
  size,
  className,
}: {
  readonly icon: LucideIcon;
  readonly tone?: "accent" | "success" | "warning" | "danger";
  readonly size?: "compact" | "large";
  readonly className?: string;
}) {
  return (
    <span data-slot="icon-tile" className={cn(tileVariants({ tone, size }), className)}>
      <Icon aria-hidden />
    </span>
  );
}

export { IconTile };
