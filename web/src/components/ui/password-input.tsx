import * as React from "react";
import { Eye, EyeOff } from "lucide-react";
import { cn } from "cn";
import { Input } from "./input";

// A password input with a show/hide toggle inside its right edge. The toggle names what it does next ("Show" or "Hide")
// and reports its state with aria-pressed; it comes right after the input in the Tab order.
function PasswordInput({
  className,
  showLabel,
  hideLabel,
  ...props
}: Omit<React.ComponentProps<"input">, "type"> & { showLabel: string; hideLabel: string }) {
  const [revealed, setRevealed] = React.useState(false);
  const Icon = revealed ? EyeOff : Eye;
  return (
    <div className="relative">
      <Input type={revealed ? "text" : "password"} className={cn("pr-(--tk-size-control)", className)} {...props} />
      <button
        type="button"
        aria-controls={props.id}
        aria-pressed={revealed}
        aria-label={revealed ? hideLabel : showLabel}
        title={revealed ? hideLabel : showLabel}
        onClick={() => setRevealed((value) => !value)}
        className="absolute inset-y-0 right-0 grid w-(--tk-size-control) cursor-pointer place-items-center rounded-r-md text-ink-muted transition-colors outline-none hover:text-ink focus-visible:text-accent focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
      >
        <Icon aria-hidden className="size-(--tk-font-size-base)" />
      </button>
    </div>
  );
}

export { PasswordInput };
