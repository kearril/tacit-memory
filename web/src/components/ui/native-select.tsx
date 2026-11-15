import * as React from "react";
import { cn } from "cn";

// A native select in the look of the inputs: the browser's own list, so it works the same on touch screens and with
// screen readers. The colours, sizes and motion are the project's tokens (see styles/tokens.css).
function NativeSelect({ className, ...props }: React.ComponentProps<"select">) {
  return (
    <select
      data-slot="native-select"
      className={cn(
        "h-control w-full min-w-0 rounded-md border-(length:--tk-border-thin) border-line bg-surface-raised px-md text-base text-ink transition-colors outline-none focus-visible:border-accent focus-visible:bg-surface focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent disabled:cursor-not-allowed disabled:opacity-(--tk-opacity-disabled)",
        className,
      )}
      {...props}
    />
  );
}

export { NativeSelect };
