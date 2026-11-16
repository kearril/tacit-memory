import { cn } from "cn";
import { type ReactNode, type RefObject, useEffect, useRef, useState } from "react";

/** The edge of the page a sticky bar keeps to. */
type Edge = "top" | "bottom";

/**
 * Whether the element has stuck to its edge. It is watched in a band that stops a pixel inside its own offset from that
 * edge, read from its style so it follows the narrow screen's top bar: once stuck its edge leaves the band, which an
 * element only scrolled partly out at the other edge does not do.
 */
function useStuck(element: RefObject<HTMLElement | null>, edge: Edge): boolean {
  const [stuck, setStuck] = useState(false);
  useEffect(() => {
    const target = element.current;
    if (target === null) {
      return;
    }
    let observer: IntersectionObserver | null = null;
    const observe = () => {
      observer?.disconnect();
      const inset = -Math.ceil(Number.parseFloat(getComputedStyle(target)[edge]) || 0) - 1;
      observer = new IntersectionObserver(
        ([entry]) => {
          const band = entry?.rootBounds;
          setStuck(
            entry !== undefined &&
              band !== null &&
              band !== undefined &&
              entry.intersectionRatio < 1 &&
              (edge === "top"
                ? entry.boundingClientRect.top < band.top
                : entry.boundingClientRect.bottom > band.bottom),
          );
        },
        { rootMargin: edge === "top" ? `${inset}px 0px 0px 0px` : `0px 0px ${inset}px 0px`, threshold: 1 },
      );
      observer.observe(target);
    };
    observe();
    window.addEventListener("resize", observe);
    return () => {
      window.removeEventListener("resize", observe);
      observer?.disconnect();
    };
  }, [element, edge]);
  return stuck;
}

/**
 * A bar that keeps to the top of the page as it scrolls (below the narrow screen's top bar), or to the bottom. In its
 * place it lies on the canvas, as wide as the column with the column's padding; once stuck it draws in to the column
 * and floats a little off the edge as a rounded surface with a raised shadow. Its content can take whether it has
 * stuck, to fold itself up. It keeps the column's gap above and below it.
 */
function StickyBar({
  edge = "top",
  role,
  className,
  children,
  ...props
}: {
  readonly edge?: Edge;
  readonly role?: "navigation" | "search" | "region";
  readonly "aria-label"?: string;
  /** How the content is laid out inside the bar. */
  readonly className?: string;
  readonly children: ReactNode | ((stuck: boolean) => ReactNode);
}) {
  const bar = useRef<HTMLDivElement>(null);
  const stuck = useStuck(bar, edge);
  return (
    <div
      ref={bar}
      role={role}
      data-stuck={stuck || undefined}
      className={cn(
        "sticky z-(--tk-layer-nav) -mx-md bg-canvas px-md py-sm transition-[margin,border-radius,box-shadow,background-color]",
        edge === "top" ? "top-[calc(var(--tk-size-nav-bar)+var(--tk-space-sm))] lg:top-sm" : "bottom-sm",
        stuck && "mx-0 rounded-lg bg-surface shadow-raised",
        className,
      )}
      {...props}
    >
      {typeof children === "function" ? children(stuck) : children}
    </div>
  );
}

export { StickyBar };
