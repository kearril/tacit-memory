import { cn } from "cn";
import { type ReactNode, type RefObject, useEffect, useRef, useState } from "react";

/**
 * Whether the element has stuck. It is watched in a band that starts a pixel below its own `top`, read from its style
 * so it follows the narrow screen's top bar: once stuck its top edge leaves the band, which an element that is only
 * scrolled partly out at the bottom does not do.
 */
function useStuck(element: RefObject<HTMLElement | null>): boolean {
  const [stuck, setStuck] = useState(false);
  useEffect(() => {
    const target = element.current;
    if (target === null) {
      return;
    }
    let observer: IntersectionObserver | null = null;
    const observe = () => {
      observer?.disconnect();
      const top = Math.ceil(Number.parseFloat(getComputedStyle(target).top) || 0);
      observer = new IntersectionObserver(
        ([entry]) =>
          setStuck(entry !== undefined && entry.intersectionRatio < 1 && entry.boundingClientRect.top <= top),
        { rootMargin: `${-top - 1}px 0px 0px 0px`, threshold: 1 },
      );
      observer.observe(target);
    };
    observe();
    window.addEventListener("resize", observe);
    return () => {
      window.removeEventListener("resize", observe);
      observer?.disconnect();
    };
  }, [element]);
  return stuck;
}

/**
 * A bar that sticks to the top of the page as it scrolls, below the narrow screen's top bar, on the canvas and as wide
 * as the column with the column's padding; a line under it once it has stuck. Its content can take whether it has
 * stuck, to fold itself up. It keeps the column's gap above and below it.
 */
function StickyBar({
  role,
  className,
  children,
  ...props
}: {
  readonly role?: "navigation" | "search";
  readonly "aria-label"?: string;
  /** How the content is laid out inside the bar. */
  readonly className?: string;
  readonly children: ReactNode | ((stuck: boolean) => ReactNode);
}) {
  const bar = useRef<HTMLDivElement>(null);
  const stuck = useStuck(bar);
  return (
    <div
      ref={bar}
      role={role}
      data-stuck={stuck || undefined}
      className={cn(
        "sticky top-(--tk-size-nav-bar) z-(--tk-layer-nav) -mx-md bg-canvas px-md py-sm lg:top-0",
        stuck && "border-b-(length:--tk-border-thin) border-line",
        className,
      )}
      {...props}
    >
      {typeof children === "function" ? children(stuck) : children}
    </div>
  );
}

export { StickyBar };
