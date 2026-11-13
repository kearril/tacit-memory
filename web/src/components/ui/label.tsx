import * as React from "react";
import { cn } from "cn";
import { Label as LabelPrimitive } from "radix-ui";

// Structure and behaviour follow shadcn/ui. The colours, sizes and motion are the project's tokens (see styles/tokens.css).
function Label({ className, ...props }: React.ComponentProps<typeof LabelPrimitive.Root>) {
  return (
    <LabelPrimitive.Root
      data-slot="label"
      className={cn(
        "flex items-center gap-sm text-sm font-medium text-ink select-none peer-disabled:cursor-not-allowed peer-disabled:opacity-(--tk-opacity-disabled)",
        className,
      )}
      {...props}
    />
  );
}

export { Label };
