import { Bot, Check } from "lucide-react";
import type { AgentIcon } from "../lib/agents";

/**
 * An agent's logo on a soft tile: its own colours, a one-colour shape in the colour of the text, or a plain robot, on a tile of
 * the control's size unless `tileClassName` and `glyphClassName` give another. `sound`, when given, puts the binding's state on the tile's corner: a tick
 * on green while it is sound, an exclamation mark on red while something is wrong; its meaning is for the text around it.
 */
export function AgentLogo({
  icon,
  sound,
  tileClassName = "size-(--tk-size-control) rounded-md",
  glyphClassName = "size-(--tk-font-size-xl)",
}: {
  readonly icon: AgentIcon | null;
  readonly sound?: boolean;
  /** The tile's size and corners. */
  readonly tileClassName?: string;
  /** The logo's size on the tile. */
  readonly glyphClassName?: string;
}) {
  const glyph = glyphClassName;
  return (
    <span className={`relative grid shrink-0 place-items-center bg-surface-raised text-ink ${tileClassName}`}>
      {icon === null ? (
        <Bot aria-hidden className={glyph} />
      ) : icon.monochrome ? (
        <span
          aria-hidden
          className={`${glyph} bg-current`}
          style={{ mask: `url("${icon.url}") center / contain no-repeat` }}
        />
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
            <svg
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth={4}
              strokeLinecap="round"
              className="size-(--tk-space-sm)"
            >
              <path d="M12 5v8M12 19h.01" />
            </svg>
          )}
        </span>
      )}
    </span>
  );
}
