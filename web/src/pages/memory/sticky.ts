import { type RefObject, useEffect, useState } from "react";

/** Clears a sticky bar under the top, and the top bar too on a narrow screen, when something is scrolled to. */
export const BELOW_BARS = "scroll-mt-[calc(2*var(--tk-size-nav-bar))] lg:scroll-mt-(--tk-size-nav-bar)";

/**
 * Whether the sticky element has stuck: the sentinel just above it has left the band the element sticks below. The band
 * is read from the element's own `top`, so it follows the narrow screen's top bar.
 */
export function useStuck(sentinel: RefObject<HTMLElement | null>, sticky: RefObject<HTMLElement | null>): boolean {
  const [stuck, setStuck] = useState(false);
  useEffect(() => {
    const target = sentinel.current;
    const element = sticky.current;
    if (target === null || element === null) {
      return;
    }
    let observer: IntersectionObserver | null = null;
    const observe = () => {
      observer?.disconnect();
      const top = Number.parseFloat(getComputedStyle(element).top) || 0;
      observer = new IntersectionObserver(([entry]) => setStuck(entry !== undefined && !entry.isIntersecting), {
        rootMargin: `${-Math.ceil(top) - 1}px 0px 0px 0px`,
      });
      observer.observe(target);
    };
    observe();
    window.addEventListener("resize", observe);
    return () => {
      window.removeEventListener("resize", observe);
      observer?.disconnect();
    };
  }, [sentinel, sticky]);
  return stuck;
}
