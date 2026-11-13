import * as React from "react";
import { cn } from "cn";

// Structure and behaviour follow shadcn/ui. The colours, sizes and motion are the project's tokens (see styles/tokens.css).
function Input({ className, type, ...props }: React.ComponentProps<"input">) {
  return (
    <input
      type={type}
      data-slot="input"
      className={cn(
        "h-control w-full min-w-0 rounded-md border-(length:--tk-border-thin) border-line bg-surface px-md text-base text-ink transition-colors outline-none placeholder:text-ink-muted selection:bg-accent selection:text-on-accent focus-visible:border-accent focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent disabled:cursor-not-allowed disabled:opacity-50 aria-invalid:border-danger aria-invalid:ring-(length:--tk-border-thick) aria-invalid:ring-danger",
        className,
      )}
      {...props}
    />
  );
}

export { Input };
