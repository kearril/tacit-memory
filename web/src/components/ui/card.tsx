import * as React from "react";
import { cn } from "cn";

// The card of the soft look: a white surface with a large radius that rests on a soft shadow. An interactive card (one
// that is itself a link or opens something) rises on hover; a card that only holds content stays still.
function Card({
  className,
  interactive = false,
  ...props
}: React.ComponentProps<"section"> & { interactive?: boolean }) {
  return (
    <section
      data-slot="card"
      className={cn(
        "grid content-start gap-md rounded-lg bg-surface p-lg text-ink shadow-rest",
        interactive && "lift hover:shadow-hover",
        className,
      )}
      {...props}
    />
  );
}

function CardTitle({ className, ...props }: React.ComponentProps<"h2">) {
  return (
    <h2
      data-slot="card-title"
      className={cn("text-lg leading-(--tk-line-height-tight) font-semibold", className)}
      {...props}
    />
  );
}

function CardDescription({ className, ...props }: React.ComponentProps<"p">) {
  return <p data-slot="card-description" className={cn("text-sm text-ink-muted", className)} {...props} />;
}

export { Card, CardDescription, CardTitle };
