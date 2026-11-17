import { LoaderCircle, type LucideIcon } from "lucide-react";

/** The icon of a button that runs a command: its own icon, or a spinner while the command runs. */
export function BusyIcon({ busy, icon: Icon }: { readonly busy: boolean; readonly icon: LucideIcon }) {
  return busy ? <LoaderCircle aria-hidden className="motion-safe:animate-spin" /> : <Icon aria-hidden />;
}
