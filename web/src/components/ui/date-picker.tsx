import { cn } from "cn";
import { CalendarDays, ChevronLeft, ChevronRight } from "lucide-react";
import { type KeyboardEvent, useEffect, useRef, useState } from "react";
import { addDays, addMonths, isDate, monthOf, monthWeeks, today } from "./date-grid";
import { FIELD_TRIGGER } from "./field-trigger";
import { Popover, PopoverContent, PopoverTrigger } from "./popover";

// A date field in the look of the select: a button that shows the date and opens a month calendar under it, in place of
// the browser's own date input. The calendar is used with the keyboard as well as the pointer: the arrows move a day or
// a week, Page Up and Page Down a month, Home and End to the ends of the week, and Enter or Space picks the day. Dates
// are `yyyy-MM-dd`; an empty value is no date.

/** What the date field says, from the dictionary. */
interface DatePickerText {
  readonly placeholder: string;
  readonly previous: string;
  readonly next: string;
  readonly today: string;
  readonly clear: string;
  /** The weekdays from Monday, as the calendar heads its columns. */
  readonly weekdays: readonly string[];
  /** A `yyyy-MM` month as the calendar titles it. */
  readonly month: (month: string) => string;
}

/** The day each key moves the focus to from `date`, or `null` for a key the calendar leaves alone. */
function stepOf(key: string, date: string): string | null {
  const weekday = (new Date(`${date}T00:00:00Z`).getUTCDay() + 6) % 7;
  switch (key) {
    case "ArrowLeft":
      return addDays(date, -1);
    case "ArrowRight":
      return addDays(date, 1);
    case "ArrowUp":
      return addDays(date, -7);
    case "ArrowDown":
      return addDays(date, 7);
    case "PageUp":
      return addMonths(date, -1);
    case "PageDown":
      return addMonths(date, 1);
    case "Home":
      return addDays(date, -weekday);
    case "End":
      return addDays(date, 6 - weekday);
    default:
      return null;
  }
}

function DatePicker({
  id,
  value,
  onValueChange,
  text,
  className,
  ...props
}: {
  readonly id?: string;
  readonly value: string;
  readonly onValueChange: (value: string) => void;
  readonly text: DatePickerText;
  readonly className?: string;
  readonly "aria-label"?: string;
  readonly "aria-invalid"?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [focus, setFocus] = useState(() => (isDate(value) ? value : today()));
  const grid = useRef<HTMLDivElement>(null);
  const now = today();
  const month = monthOf(focus);

  // Keeps the keyboard on the focused day when a key in the calendar moved it, even to another month whose days replace
  // the one that had the focus; the calendar puts it there itself when it opens.
  const moved = useRef(false);
  useEffect(() => {
    if (moved.current) {
      moved.current = false;
      grid.current?.querySelector<HTMLElement>(`[data-date="${focus}"]`)?.focus();
    }
  }, [focus]);

  const pick = (date: string) => {
    onValueChange(date);
    setOpen(false);
  };
  const onKeyDown = (event: KeyboardEvent<HTMLButtonElement>, date: string) => {
    const next = stepOf(event.key, date);
    if (next !== null) {
      event.preventDefault();
      moved.current = true;
      setFocus(next);
    }
  };
  const nav =
    "grid size-(--tk-size-control) cursor-pointer place-items-center rounded-sm text-ink-muted outline-none hover:bg-surface-raised hover:text-ink focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent";
  const action =
    "cursor-pointer rounded-sm px-sm py-xs text-sm text-accent outline-none hover:bg-surface-raised focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent";

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        if (next) {
          setFocus(isDate(value) ? value : today());
        }
        setOpen(next);
      }}
    >
      <PopoverTrigger id={id} data-slot="date-picker-trigger" className={cn(FIELD_TRIGGER, className)} {...props}>
        <CalendarDays aria-hidden className="size-(--tk-font-size-base) shrink-0 text-ink-muted" />
        <span className={cn("min-w-0 truncate tabular-nums", value === "" && "text-ink-muted")}>
          {value === "" ? text.placeholder : value}
        </span>
      </PopoverTrigger>
      <PopoverContent
        data-slot="date-picker-content"
        align="start"
        sideOffset={4}
        onOpenAutoFocus={(event) => {
          event.preventDefault();
          grid.current?.querySelector<HTMLElement>('[tabindex="0"]')?.focus();
        }}
        className="max-w-none p-sm"
      >
        <div className="flex items-center justify-between gap-sm">
          <button
            type="button"
            aria-label={text.previous}
            title={text.previous}
            onClick={() => setFocus(addMonths(focus, -1))}
            className={nav}
          >
            <ChevronLeft aria-hidden className="size-(--tk-font-size-base)" />
          </button>
          <span aria-live="polite" className="text-sm font-semibold tabular-nums">
            {text.month(month)}
          </span>
          <button
            type="button"
            aria-label={text.next}
            title={text.next}
            onClick={() => setFocus(addMonths(focus, 1))}
            className={nav}
          >
            <ChevronRight aria-hidden className="size-(--tk-font-size-base)" />
          </button>
        </div>
        <div ref={grid} role="group" aria-label={text.month(month)} className="grid grid-cols-7 gap-xs">
          {text.weekdays.map((weekday) => (
            <span key={weekday} aria-hidden className="text-center text-xs text-ink-muted">
              {weekday}
            </span>
          ))}
          {monthWeeks(focus)
            .flat()
            .map((date) => {
              const selected = date === value;
              return (
                <button
                  key={date}
                  type="button"
                  data-date={date}
                  tabIndex={date === focus ? 0 : -1}
                  aria-label={date}
                  aria-pressed={selected}
                  aria-current={date === now ? "date" : undefined}
                  onClick={() => pick(date)}
                  onKeyDown={(event) => onKeyDown(event, date)}
                  className={cn(
                    "grid size-(--tk-size-control) cursor-pointer place-items-center rounded-sm text-sm tabular-nums outline-none focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
                    selected
                      ? "bg-accent font-medium text-on-accent"
                      : cn(
                          "hover:bg-surface-raised",
                          monthOf(date) !== month && "text-ink-muted/60",
                          date === now && "font-semibold text-accent",
                        ),
                  )}
                >
                  {Number(date.slice(8))}
                </button>
              );
            })}
        </div>
        <div className="flex items-center justify-between gap-sm border-t-(length:--tk-border-thin) border-line pt-xs">
          <button type="button" onClick={() => pick(now)} className={action}>
            {text.today}
          </button>
          {value === "" ? null : (
            <button type="button" onClick={() => pick("")} className={cn(action, "text-ink-muted")}>
              {text.clear}
            </button>
          )}
        </div>
      </PopoverContent>
    </Popover>
  );
}

export { DatePicker, type DatePickerText };
