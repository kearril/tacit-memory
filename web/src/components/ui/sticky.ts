/**
 * Clears the sticky bar floating under the top, at its height now, and the top bar too on a narrow screen, when something
 * is scrolled to. With no bar on the page it clears a bar of one row.
 */
export const BELOW_BARS =
  "scroll-mt-[calc(var(--tk-size-nav-bar)+var(--sticky-bar-height,var(--tk-size-nav-bar))+2*var(--tk-space-sm))] lg:scroll-mt-[calc(var(--sticky-bar-height,var(--tk-size-nav-bar))+2*var(--tk-space-sm))]";
