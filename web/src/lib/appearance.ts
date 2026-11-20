import { useSyncExternalStore } from "react";
import { flushSync } from "react-dom";

/**
 * The look the person picked in this browser: a colour scheme that overrides the system's, or none to follow it, and a
 * palette. Both live in the document, where the styles read them (the head's colour-scheme meta, `data-palette` on the
 * root), and in local storage, where the script in `index.html` finds them before the first paint. Nothing is sent to
 * the service.
 */

export type ColorScheme = "light" | "dark";

export const PALETTES = ["default", "catppuccin", "nord", "everforest", "rosepine"] as const;
export type Palette = (typeof PALETTES)[number];

/** The names of the palettes, which are their own and are not translated. */
export const PALETTE_NAMES: Readonly<Record<Palette, string>> = {
  default: "Default",
  catppuccin: "Catppuccin",
  nord: "Nord",
  everforest: "Everforest",
  rosepine: "Rosé Pine",
};

export interface Appearance {
  /** The scheme the person pinned, or `null` to follow the system. */
  readonly override: ColorScheme | null;
  /** The system's scheme. */
  readonly system: ColorScheme;
  /** The scheme on screen. */
  readonly shown: ColorScheme;
  readonly palette: Palette;
}

/** Where on screen a change was asked for, so the new look grows from there. */
export interface Origin {
  readonly x: number;
  readonly y: number;
}

// The keys the script in index.html reads too.
const SCHEME_KEY = "tacit.colorScheme";
const PALETTE_KEY = "tacit.palette";
const SCHEME_META = 'meta[name="color-scheme"]';

const darkQuery = () => window.matchMedia("(prefers-color-scheme: dark)");

function isPalette(value: string | null | undefined): value is Palette {
  return (PALETTES as readonly (string | null | undefined)[]).includes(value);
}

function read(): Appearance {
  const content = document.querySelector<HTMLMetaElement>(SCHEME_META)?.content;
  const override = content === "light" || content === "dark" ? content : null;
  const system: ColorScheme = darkQuery().matches ? "dark" : "light";
  const palette = document.documentElement.dataset.palette;
  return { override, system, shown: override ?? system, palette: isPalette(palette) ? palette : "default" };
}

function apply(override: ColorScheme | null, palette: Palette) {
  const meta = document.querySelector<HTMLMetaElement>(SCHEME_META);
  if (meta !== null) {
    meta.content = override ?? "light dark";
  }
  document.documentElement.dataset.palette = palette;
}

/** Local storage may be refused (site data blocked); the look still changes for this page, it is just not kept. */
function store(key: string, value: string | null) {
  try {
    if (value === null) {
      localStorage.removeItem(key);
    } else {
      localStorage.setItem(key, value);
    }
  } catch {
    // Not kept; nothing else depends on it.
  }
}

let snapshot: Appearance | undefined;
const listeners = new Set<() => void>();

function changed() {
  snapshot = read();
  for (const listener of listeners) {
    listener();
  }
}

/** Another tab of the interface changed the look: follow it. A cleared storage (`key` null) goes back to the defaults. */
function onStorage(event: StorageEvent) {
  if (event.key !== null && event.key !== SCHEME_KEY && event.key !== PALETTE_KEY) {
    return;
  }
  const scheme = event.storageArea?.getItem(SCHEME_KEY) ?? null;
  const palette = event.storageArea?.getItem(PALETTE_KEY) ?? null;
  apply(scheme === "light" || scheme === "dark" ? scheme : null, isPalette(palette) ? palette : "default");
  changed();
}

function subscribe(listener: () => void) {
  if (listeners.size === 0) {
    darkQuery().addEventListener("change", changed);
    window.addEventListener("storage", onStorage);
  }
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
    if (listeners.size === 0) {
      darkQuery().removeEventListener("change", changed);
      window.removeEventListener("storage", onStorage);
    }
  };
}

function getSnapshot(): Appearance {
  snapshot ??= read();
  return snapshot;
}

export function useAppearance(): Appearance {
  return useSyncExternalStore(subscribe, getSnapshot);
}

/**
 * Runs a change of look. Where the browser has view transitions and motion is not reduced, the new look grows as a circle
 * from `origin` over the old one; otherwise it changes at once.
 */
function reveal(origin: Origin | undefined, change: () => void) {
  const run = () => {
    change();
    flushSync(changed);
  };
  if (
    origin === undefined ||
    typeof document.startViewTransition !== "function" ||
    window.matchMedia("(prefers-reduced-motion: reduce)").matches
  ) {
    run();
    return;
  }

  const root = document.documentElement;
  const { x, y } = origin;
  const radius = Math.hypot(Math.max(x, window.innerWidth - x), Math.max(y, window.innerHeight - y));
  const style = getComputedStyle(root);
  root.classList.add("appearance-reveal");
  const transition = document.startViewTransition(run);
  void transition.ready.then(() => {
    root.animate(
      { clipPath: [`circle(0px at ${x}px ${y}px)`, `circle(${radius}px at ${x}px ${y}px)`] },
      {
        duration: Number.parseFloat(style.getPropertyValue("--tk-duration-reveal")),
        easing: style.getPropertyValue("--tk-ease-standard"),
        pseudoElement: "::view-transition-new(root)",
      },
    );
  });
  void transition.finished.finally(() => root.classList.remove("appearance-reveal"));
}

/**
 * The scheme a two-state switch moves to from what is on screen: the other one, pinned only while it differs from the
 * system's. Choosing the system's own scheme means undoing the change, so it goes back to following the system instead
 * of pinning a scheme that would stay when the system later switches.
 */
export function toggledOverride(shown: ColorScheme, system: ColorScheme): ColorScheme | null {
  const target: ColorScheme = shown === "dark" ? "light" : "dark";
  return target === system ? null : target;
}

/** Pins the scheme, or with `null` follows the system again. */
export function setColorScheme(override: ColorScheme | null, origin?: Origin) {
  store(SCHEME_KEY, override);
  reveal(origin, () => apply(override, read().palette));
}

/** Switches between light and dark from what is on screen. */
export function toggleColorScheme(origin?: Origin) {
  const now = read();
  setColorScheme(toggledOverride(now.shown, now.system), origin);
}

export function setPalette(palette: Palette, origin?: Origin) {
  if (read().palette === palette) {
    return;
  }
  store(PALETTE_KEY, palette === "default" ? null : palette);
  reveal(origin, () => apply(read().override, palette));
}

/**
 * Where a change grows from: where the pointer clicked, or for a press from the keyboard or a menu (which has no
 * position) the centre of the element.
 */
export function originOf(
  element: Element,
  pointer?: { readonly clientX: number; readonly clientY: number; readonly detail: number },
): Origin {
  if (pointer !== undefined && pointer.detail > 0) {
    return { x: pointer.clientX, y: pointer.clientY };
  }
  const rect = element.getBoundingClientRect();
  return { x: rect.left + rect.width / 2, y: rect.top + rect.height / 2 };
}
