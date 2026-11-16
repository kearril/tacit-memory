import { cn } from "cn";

// A switch between a few views of the same content, in a card's head: the options sit in a rounded track, and the chosen
// one is raised on the surface colour. Each option is a toggle button that reports whether it is chosen with
// aria-pressed, and the track is a group named by `label`.

interface SegmentedOption<T extends string> {
  readonly value: T;
  readonly label: string;
}

function Segmented<T extends string>({
  label,
  value,
  options,
  onValueChange,
  className,
}: {
  readonly label: string;
  readonly value: T;
  readonly options: readonly SegmentedOption<T>[];
  readonly onValueChange: (value: T) => void;
  readonly className?: string;
}) {
  return (
    <div role="group" aria-label={label} className={cn("flex shrink-0 rounded-full bg-surface-raised p-xs", className)}>
      {options.map((option) => (
        <button
          key={option.value}
          type="button"
          aria-pressed={value === option.value}
          onClick={() => onValueChange(option.value)}
          className={cn(
            "cursor-pointer rounded-full px-sm py-xs text-xs whitespace-nowrap tabular-nums transition-colors outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
            value === option.value ? "bg-surface font-medium text-ink shadow-rest" : "text-ink-muted hover:text-ink",
          )}
        >
          {option.label}
        </button>
      ))}
    </div>
  );
}

export { Segmented, type SegmentedOption };
