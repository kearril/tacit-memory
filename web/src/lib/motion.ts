/**
 * Scroll behavior that respects the system's reduced-motion preference: returns `"instant"` when instant scrolling is
 * requested or reduced motion is active, and `"smooth"` otherwise.
 */
export function scrollBehavior(preferred: "smooth" | "instant" = "smooth"): ScrollBehavior {
  if (preferred === "instant") {
    return "instant";
  }
  return window.matchMedia("(prefers-reduced-motion: reduce)").matches ? "instant" : "smooth";
}
