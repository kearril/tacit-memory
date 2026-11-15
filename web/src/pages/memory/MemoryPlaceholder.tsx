import { useDictionary } from "../../app/session-context";
import type { MemoryView } from "./views";

/** A view of the memory page whose content is not implemented yet: its name, and the sentence that says so. */
export function MemoryPlaceholder({ view }: { readonly view: MemoryView }) {
  const dictionary = useDictionary();
  return (
    <div className="grid gap-md">
      <h1 className="text-xl leading-(--tk-line-height-tight) font-semibold">{dictionary.memory.views[view]}</h1>
      <p className="text-sm text-ink-muted">{dictionary.notImplemented}</p>
    </div>
  );
}
