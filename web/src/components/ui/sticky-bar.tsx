import { cn } from "cn";
import { type ReactNode, type RefObject, useEffect, useLayoutEffect, useRef, useState } from "react";
import { publishBarBottom } from "./sticky";

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
 * How much a bar folded once stuck, for a bottom margin that keeps what follows it in place: without it the page would
 * shift up by the fold and could get short enough to scroll the bar back out of its stuck place, unfolding it again.
 */
function useFold(element: RefObject<HTMLElement | null>, stuck: boolean): number {
  const rest = useRef(0);
  const [fold, setFold] = useState(0);
  useEffect(() => {
    const target = element.current;
    if (target === null) {
      return;
    }
    const observer = new ResizeObserver(() => {
      const height = target.offsetHeight;
      if (!stuck) {
        rest.current = height;
      }
      setFold(stuck ? Math.max(0, rest.current - height) : 0);
    });
    observer.observe(target);
    return () => observer.disconnect();
  }, [element, stuck]);
  return fold;
}

/**
 * A top bar publishes where it ends on screen, with the gap of the column it stands in, for what is scrolled to or placed
 * under it. It measures as it folds or unfolds and as the window resizes, which moves its edge on the narrow screen, and
 * after each render before the page's own effects run, so a page that scrolls as its content arrives already finds the
 * bar's new height; sticking does not take it away, so what is placed under the bar never loses its place meanwhile.
 */
function usePublishBottom(element: RefObject<HTMLElement | null>, edge: Edge) {
  const published = useRef("");
  useLayoutEffect(() => {
    if (edge === "top" && element.current !== null) {
      publishBottom(element.current, published);
    }
  });
  useEffect(() => {
    const target = element.current;
    if (target === null || edge !== "top") {
      return;
    }
    const publish = () => publishBottom(target, published);
    const observer = new ResizeObserver(publish);
    observer.observe(target);
    window.addEventListener("resize", publish);
    return () => {
      observer.disconnect();
      window.removeEventListener("resize", publish);
      published.current = "";
      publishBarBottom(null);
    };
  }, [element, edge]);
}

/** Publishes where `bar` ends on screen with its column's gap, unless that is what `published` says was published last. */
function publishBottom(bar: HTMLElement, published: { current: string }) {
  const gap = bar.parentElement === null ? 0 : parseFloat(getComputedStyle(bar.parentElement).rowGap) || 0;
  const bottom = (parseFloat(getComputedStyle(bar).top) || 0) + bar.offsetHeight + gap;
  if (published.current !== `${bottom} ${gap}`) {
    published.current = `${bottom} ${gap}`;
    publishBarBottom(bottom, gap);
  }
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
  const bar = useRef<HTMLElement>(null);
  const stuck = useStuck(bar, edge);
  const fold = useFold(bar, stuck);
  usePublishBottom(bar, edge);
  const sharedClassName = cn(
    "sticky z-(--tk-layer-nav) -mx-md bg-canvas px-md py-sm transition-[margin,border-radius,box-shadow,background-color]",
    edge === "top" ? "top-[calc(var(--tk-size-nav-bar)+var(--tk-space-sm))] lg:top-sm" : "bottom-sm",
    stuck && "mx-0 rounded-lg bg-surface shadow-raised",
    className,
  );
  const content = typeof children === "function" ? children(stuck) : children;
  if (role === "search") {
    return (
      <search
        ref={bar}
        style={{ marginBottom: fold }}
        data-stuck={stuck || undefined}
        className={sharedClassName}
        {...props}
      >
        {content}
      </search>
    );
  }
  return (
    <div
      ref={bar as RefObject<HTMLDivElement | null>}
      style={{ marginBottom: fold }}
      role={role}
      data-stuck={stuck || undefined}
      className={sharedClassName}
      {...props}
    >
      {content}
    </div>
  );
}

export { StickyBar };
