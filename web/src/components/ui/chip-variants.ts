import { cva } from "class-variance-authority";

// The pill of the soft look that filters, switches or jumps: a white surface on the rest shadow, the accent's solid fill
// when it is chosen. The other tones mark a pill that leads somewhere current, one that is not yet real, and one whose
// values were changed or are wrong.
const chipVariants = cva(
  "inline-flex shrink-0 cursor-pointer items-center gap-xs rounded-full px-md py-xs text-sm whitespace-nowrap transition-[color,background-color,box-shadow] outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent [&_svg]:size-(--tk-font-size-sm) [&_svg]:shrink-0",
  {
    variants: {
      tone: {
        rest: "bg-surface text-ink-muted shadow-rest hover:text-ink hover:shadow-hover",
        on: "bg-accent text-on-accent",
        current: "bg-accent-soft text-accent ring-(length:--tk-border-thick) ring-accent",
        faint: "border-(length:--tk-border-thin) border-dashed border-ink-muted/40 text-ink-muted hover:text-ink",
        warning: "bg-warning-soft text-warning",
        danger: "bg-danger-soft text-danger",
      },
    },
    defaultVariants: {
      tone: "rest",
    },
  },
);

export { chipVariants };
