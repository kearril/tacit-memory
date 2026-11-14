import { Bot, Check } from "lucide-react";
import type { AgentIcon } from "./agents";

/** The grid the large logo tiles wrap in: as many columns as fit, each a few logos wide. */
export const LOGO_TILES = "grid grid-cols-[repeat(auto-fill,minmax(calc(var(--tk-size-logo)*2.5),1fr))] gap-sm";

/**
 * An agent's logo on a soft tile: its own colours, a one-colour shape in the colour of the text, or a plain robot. `large`
 * is the tile of the settings page's bindings. `sound`, when given, puts the binding's state on the tile's corner: a tick
 * on green while it is sound, an exclamation mark on red while something is wrong; its meaning is for the text around it.
 */
export function AgentLogo({
  icon,
  large = false,
  sound,
}: {
  readonly icon: AgentIcon | null;
  readonly large?: boolean;
  readonly sound?: boolean;
}) {
  const glyph = large ? "size-(--tk-size-icon-tile)" : "size-(--tk-font-size-xl)";
  return (
    <span
      className={`relative grid shrink-0 place-items-center bg-surface-raised text-ink ${
        large ? "size-(--tk-size-logo) rounded-lg" : "size-(--tk-size-control) rounded-md"
      }`}
    >
      {icon === null ? (
        <Bot aria-hidden className={glyph} />
      ) : icon.monochrome ? (
        <span aria-hidden className={`${glyph} bg-current`} style={{ mask: `url("${icon.url}") center / contain no-repeat` }} />
      ) : (
        <img alt="" src={icon.url} className={glyph} />
      )}
      {sound === undefined ? null : (
        <span
          aria-hidden
          className={`absolute -right-xs -bottom-xs grid size-(--tk-space-md) place-items-center rounded-full border-(length:--tk-border-thick) border-surface text-on-accent ${
            sound ? "bg-success" : "bg-danger"
          }`}
        >
          {sound ? (
            <Check className="size-(--tk-space-sm)" strokeWidth={4} />
          ) : (
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={4} strokeLinecap="round" className="size-(--tk-space-sm)">
              <path d="M12 5v8M12 19h.01" />
            </svg>
          )}
        </span>
      )}
    </span>
  );
}
