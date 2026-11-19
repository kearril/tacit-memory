import * as React from "react";
import { cn } from "cn";

// The cards of the soft look, a family told apart by how far each rises from the page, so a view shows what leads and
// what supports it:
// - raised: a white surface on a soft shadow, for what the view is about; no more than two in sight at once.
// - tinted: a wash of the accent and no shadow, for a summary or a conclusion; one at most, at the top.
// - flat: the white surface without a shadow, for what supports the leads.
// - outline: a strong thin border and no fill, its label set into the top border, for many sections of the same shape.
// - tab: a raised sheet whose label stands out of its top left corner as a tab, for the large parts a view divides into.
// An interactive raised card (one that is itself a link or opens something) rises on hover.

/** The padding, spacing and text every card body has. */
const BODY = "grid grid-cols-1 content-start gap-md rounded-lg p-lg text-ink";

type CardProps = Omit<React.ComponentProps<"section">, "children"> & {
  readonly children?: React.ReactNode;
  readonly interactive?: boolean;
} & (
    | { readonly variant?: "raised" | "tinted" | "flat"; readonly label?: undefined }
    | {
        /** outline sets `label` into the top border; tab sets it in the tab. */
        readonly variant: "outline" | "tab";
        readonly label: React.ReactNode;
      }
  );

function Card({ className, interactive = false, variant = "raised", label, children, ...props }: CardProps) {
  if (variant === "tab") {
    return (
      <section data-slot="card" className={cn("grid grid-cols-1 [filter:var(--tk-filter-rest)]", className)} {...props}>
        <div className="flex items-end">
          <div className="flex min-w-0 items-center gap-sm rounded-t-md bg-surface px-md pt-sm pb-xs">{label}</div>
          <span aria-hidden className="card-notch shrink-0" />
        </div>
        <div className={cn(BODY, "rounded-tl-none bg-surface pt-md")}>{children}</div>
      </section>
    );
  }
  if (variant === "outline") {
    return (
      <fieldset
        data-slot="card"
        className={cn(BODY, "m-0 min-w-0 border-(length:--tk-border-thin) border-line-strong pt-xs", className)}
        {...(props as React.ComponentProps<"fieldset">)}
      >
        <legend className="-ml-sm flex items-center gap-sm px-sm">{label}</legend>
        {children}
      </fieldset>
    );
  }
  return (
    <section
      data-slot="card"
      className={cn(
        BODY,
        variant === "raised" && "bg-surface shadow-rest",
        variant === "raised" && interactive && "lift hover:shadow-hover",
        variant === "tinted" && "bg-accent-soft",
        variant === "flat" && "bg-surface",
        className,
      )}
      {...props}
    >
      {children}
    </section>
  );
}

function CardTitle({
  as: Tag = "h2",
  className,
  ...props
}: React.ComponentProps<"h2"> & { readonly as?: "h1" | "h2" }) {
  return (
    <Tag
      data-slot="card-title"
      className={cn("text-lg leading-(--tk-line-height-tight) font-semibold text-balance", className)}
      {...props}
    />
  );
}

function CardDescription({ className, ...props }: React.ComponentProps<"p">) {
  return <p data-slot="card-description" className={cn("text-sm text-ink-muted", className)} {...props} />;
}

/**
 * Cards of the same rank gathered under one name: the name stands above them outside any card, small and muted, and each
 * card keeps its own title. `className` places the group, `layout` lays its cards out.
 */
function CardGroup({
  id,
  label,
  className,
  layout,
  children,
}: {
  readonly id: string;
  readonly label: string;
  readonly className?: string;
  readonly layout?: string;
  readonly children: React.ReactNode;
}) {
  return (
    <section aria-labelledby={id} className={cn("grid grid-cols-1 content-start gap-sm", className)}>
      <h2 id={id} className="px-sm text-xs font-semibold tracking-wide text-ink-muted">
        {label}
      </h2>
      <div className={cn("grid grid-cols-1 gap-lg", layout)}>{children}</div>
    </section>
  );
}

export { Card, CardDescription, CardGroup, CardTitle };
