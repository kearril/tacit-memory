import { cva } from "class-variance-authority";

// The dashed box one entry of the memory sits in (a persona entry, a page's section, a search result, an activity), so
// entries read as pieces of one card without each being a card of its own. A lit box is the one the pointer or a link
// points at; a warning box has a problem; an accent box heads a group, as a week's summary does.
const entryBox = cva("rounded-md border-(length:--tk-border-thin) border-dashed px-md py-sm", {
  variants: {
    tone: {
      quiet: "border-ink-muted/40",
      lit: "border-accent bg-accent-soft/40",
      warning: "border-warning bg-warning-soft",
      accent: "border-accent/50",
    },
  },
  defaultVariants: {
    tone: "quiet",
  },
});

export { entryBox };
