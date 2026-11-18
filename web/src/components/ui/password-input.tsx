import * as React from "react";
import { Eye, EyeOff } from "lucide-react";
import { cn } from "cn";
import { Input } from "./input";

/**
 * The one input for a password or a secret: masked by default, with a show/hide toggle inside its right edge that only
 * switches what the box itself holds between plain text and the mask. The toggle names what it does next and reports its
 * state with aria-pressed; it comes right after the input in the Tab order. Submitting the form the input sits in masks
 * it again, so the browser does not keep the plain text as an ordinary field's entry.
 */
function PasswordInput({
  className,
  showLabel,
  hideLabel,
  ...props
}: Omit<React.ComponentProps<"input">, "type"> & { showLabel: string; hideLabel: string }) {
  const [revealed, setRevealed] = React.useState(false);
  const wrapper = React.useRef<HTMLDivElement>(null);
  const Icon = revealed ? EyeOff : Eye;

  React.useEffect(() => {
    const form = wrapper.current?.closest("form");
    const mask = () => setRevealed(false);
    form?.addEventListener("submit", mask);
    return () => form?.removeEventListener("submit", mask);
  }, []);

  return (
    <div ref={wrapper} className="relative">
      <Input
        type={revealed ? "text" : "password"}
        spellCheck={false}
        autoCapitalize="off"
        className={cn("pr-(--tk-size-control)", className)}
        {...props}
      />
      <button
        type="button"
        aria-controls={props.id}
        aria-pressed={revealed}
        aria-label={revealed ? hideLabel : showLabel}
        title={revealed ? hideLabel : showLabel}
        disabled={props.disabled}
        onClick={() => setRevealed((value) => !value)}
        className="absolute inset-y-0 right-0 grid w-(--tk-size-control) cursor-pointer place-items-center rounded-r-md text-ink-muted transition-colors outline-none hover:text-ink focus-visible:text-accent focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent disabled:cursor-not-allowed disabled:opacity-(--tk-opacity-disabled)"
      >
        <Icon aria-hidden className="size-(--tk-font-size-base)" />
      </button>
    </div>
  );
}

export { PasswordInput };
