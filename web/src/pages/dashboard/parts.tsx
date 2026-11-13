import { LoaderCircle, type LucideIcon } from "lucide-react";
import type { ReactNode } from "react";

/** The icon of a button that runs a command: its own icon, or a spinner while the command runs. */
export function BusyIcon({ busy, icon: Icon }: { readonly busy: boolean; readonly icon: LucideIcon }) {
  return busy ? <LoaderCircle aria-hidden className="motion-safe:animate-spin" /> : <Icon aria-hidden />;
}

/**
 * One line of a two-column list (a `dl` laid out as `auto` and the rest): its label on the left, its value on the right
 * with what goes after the value. A long value truncates in a span of its own.
 */
export function InfoRow({ label, children }: { readonly label: string; readonly children: ReactNode }) {
  return (
    <div className="contents">
      <dt className="py-xs text-ink-muted">{label}</dt>
      <dd className="flex min-w-0 items-center gap-xs">{children}</dd>
    </div>
  );
}
