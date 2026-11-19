import { type RefObject, useEffect } from "react";
import { scrollBehavior } from "../../lib/motion";

// Where the floating top bar ends is `--sticky-bar-bottom` on the document. The stylesheet gives it the bottom of a bar
// of one row, for a page without a bar and until a bar measures itself; a bar's own measure overrides it.

/**
 * Clears the sticky bar floating under the top, and the top bar too on a narrow screen, when something is scrolled to:
 * it starts where the bar ends.
 */
export const BELOW_BARS = "scroll-mt-(--sticky-bar-bottom)";

/** Pins a `sticky` element just under the floating top bar, following it as it folds and unfolds. */
export const UNDER_BAR = "top-(--sticky-bar-bottom)";

const listeners = new Set<() => void>();

/** The gap the page leaves under the bar, kept with its bottom. */
let barGap = 0;

/**
 * Publishes where the floating top bar ends on screen, with the gap the page leaves under it, in `--sticky-bar-bottom`
 * on the document, and tells what is placed under it; `null` takes it away when the bar goes, back to the default.
 */
export function publishBarBottom(bottom: number | null, gap = 0) {
  const root = document.documentElement.style;
  barGap = bottom === null ? 0 : gap;
  if (bottom === null) {
    root.removeProperty("--sticky-bar-bottom");
  } else {
    root.setProperty("--sticky-bar-bottom", `${bottom}px`);
  }
  for (const listener of listeners) {
    listener();
  }
}

/** Where the floating top bar ends on screen, with the gap under it; with no bar, where a bar of one row would end. */
export function barBottom(): number {
  return parseFloat(getComputedStyle(document.documentElement).getPropertyValue("--sticky-bar-bottom")) || 0;
}

/** Scrolls the page smoothly so that `element` starts under the floating top bar, jumping under reduced motion. */
export function scrollUnderBar(element: HTMLElement) {
  window.scrollTo({
    top: element.getBoundingClientRect().top + window.scrollY - barBottom(),
    behavior: scrollBehavior(),
  });
}

/**
 * Places a `sticky` element under the floating top bar while `enabled`, as the bar folds and unfolds; an element taller
 * than the room left under the bar sticks with its bottom the bar's gap above the window's instead, so its end stays
 * in reach.
 */
export function useStickUnderBar(element: RefObject<HTMLElement | null>, enabled: boolean) {
  useEffect(() => {
    const target = element.current;
    if (!enabled || target === null) {
      return;
    }
    const place = () => {
      target.style.setProperty("top", `${Math.min(barBottom(), window.innerHeight - target.offsetHeight - barGap)}px`);
    };
    place();
    const observer = new ResizeObserver(place);
    observer.observe(target);
    listeners.add(place);
    window.addEventListener("resize", place);
    return () => {
      observer.disconnect();
      listeners.delete(place);
      window.removeEventListener("resize", place);
      target.style.removeProperty("top");
    };
  }, [element, enabled]);
}
