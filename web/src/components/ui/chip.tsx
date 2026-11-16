import * as React from "react";
import { cn } from "cn";
import { chipVariants } from "./chip-variants";

/** A pill that is pressed or not, as one of a group of filters; a pill of another tone or element takes `chipVariants`. */
function Chip({
  className,
  pressed,
  ...props
}: Omit<React.ComponentProps<"button">, "aria-pressed"> & { readonly pressed: boolean }) {
  return (
    <button
      type="button"
      data-slot="chip"
      aria-pressed={pressed}
      className={cn(chipVariants({ tone: pressed ? "on" : "rest" }), className)}
      {...props}
    />
  );
}

export { Chip };
