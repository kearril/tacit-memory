/**
 * The trigger of a field that opens a panel to pick from (a select, a date picker): it looks like a text box, its border
 * turns to the accent while the panel is open, and to the danger colour while the value is wrong.
 */
export const FIELD_TRIGGER =
  "flex h-control w-full min-w-0 cursor-pointer items-center gap-sm rounded-md border-(length:--tk-border-thin) border-line bg-surface-raised px-md text-base text-ink transition-colors outline-none focus-visible:border-accent focus-visible:bg-surface focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent disabled:cursor-not-allowed disabled:opacity-(--tk-opacity-disabled) aria-invalid:border-danger data-[state=open]:border-accent";
