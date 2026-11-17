import { useEffect, useSyncExternalStore } from "react";
import { type SectionId, sectionIdOf, SECTIONS } from "../../lib/routes";

/** What the sidebar's list of settings sections shows: the section being read, and how many problems each section has. */
export interface SectionNav {
  readonly active: SectionId | null;
  readonly problems: Partial<Record<SectionId, number>>;
}

let state: SectionNav = { active: null, problems: {} };
const listeners = new Set<() => void>();

function publish(next: SectionNav) {
  if (next.active === state.active && sameCounts(next.problems, state.problems)) {
    return;
  }
  state = next;
  listeners.forEach((listener) => listener());
}

function sameCounts(a: SectionNav["problems"], b: SectionNav["problems"]): boolean {
  return SECTIONS.every((section) => (a[section] ?? 0) === (b[section] ?? 0));
}

/** The sections' state as the settings page last published it; the sidebar reads it. */
export function useSectionNav(): SectionNav {
  return useSyncExternalStore(
    (listener) => {
      listeners.add(listener);
      return () => listeners.delete(listener);
    },
    () => state,
  );
}

/** Publishes how many problems each section has while the settings page shows them. */
export function usePublishProblems(problems: SectionNav["problems"]) {
  useEffect(() => {
    publish({ ...state, problems });
  }, [problems]);
  useEffect(() => () => publish({ active: null, problems: {} }), []);
}

/**
 * Follows which section is being read while the page scrolls: the last one whose top has passed a line near the top of
 * the window, or the last section once the page is scrolled to its end.
 */
export function useScrollSpy(ready: boolean) {
  useEffect(() => {
    if (!ready) {
      return;
    }
    let frame = 0;
    const update = () => {
      frame = 0;
      const line = window.innerHeight * 0.25;
      const atEnd = window.innerHeight + window.scrollY >= document.documentElement.scrollHeight - 2;
      let active: SectionId | null = null;
      for (const section of SECTIONS) {
        const element = document.getElementById(sectionIdOf(section));
        if (element !== null && (atEnd || element.getBoundingClientRect().top <= line)) {
          active = section;
        }
      }
      publish({ ...state, active: active ?? SECTIONS[0] });
    };
    const onScroll = () => {
      if (frame === 0) {
        frame = requestAnimationFrame(update);
      }
    };
    update();
    window.addEventListener("scroll", onScroll, { passive: true });
    window.addEventListener("resize", onScroll);
    return () => {
      cancelAnimationFrame(frame);
      window.removeEventListener("scroll", onScroll);
      window.removeEventListener("resize", onScroll);
    };
  }, [ready]);
}
