import { useEffect } from "react";
import { useLocation } from "react-router";

/**
 * Scrolls to the element the location's hash names once the cards are there, so a link that leads to a card on a page
 * reaches it, centred unless `block` says otherwise. With reduced motion asked for, it jumps instead of gliding.
 */
export function useScrollToHash(ready: boolean, block: ScrollLogicalPosition = "center") {
  const { hash } = useLocation();
  useEffect(() => {
    if (ready && hash !== "") {
      const reduced = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
      document
        .getElementById(decodeURIComponent(hash.slice(1)))
        ?.scrollIntoView({ behavior: reduced ? "auto" : "smooth", block });
    }
  }, [ready, hash, block]);
}
