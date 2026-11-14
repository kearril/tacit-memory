import { useEffect, useRef, useState } from "react";
import type { ServiceLogDay, ServiceLogEntry } from "../../api/logs";
import { getLogDay } from "../../api/logs";
import { appendPage, dayTurned, FOLLOW_INTERVAL, type HeldLog } from "./follow";

/** What a followed day holds, and whether the last read for new events failed. */
export interface FollowedLog {
  readonly held: HeldLog | undefined;
  readonly offline: boolean;
}

/**
 * A day's events, kept growing while `active`: every two seconds, and at once when the tab is shown again, the page asks
 * for the events past those it holds and adds them. Nothing is asked while the tab is hidden. A failed read keeps what is
 * held and is tried again on the next tick. `onFresh` hears each batch of new events; `onDayTurned` hears the new day once
 * the service's today has moved past this one, after the last events of this day have been added.
 */
export function useFollowedLog(
  base: ServiceLogDay | undefined,
  active: boolean,
  onFresh: (entries: readonly ServiceLogEntry[]) => void,
  onDayTurned: (today: string) => void,
): FollowedLog {
  // The events read since the base came, tied to that base: a new base (another day, or the day read again) starts over.
  const [grown, setGrown] = useState<{ readonly base: ServiceLogDay; readonly held: HeldLog }>();
  const [offline, setOffline] = useState(false);
  const held = base === undefined ? undefined : grown?.base === base ? grown.held : base;

  const latest = useRef({ held, onFresh, onDayTurned });
  useEffect(() => {
    latest.current = { held, onFresh, onDayTurned };
  });

  useEffect(() => {
    if (!active || base === undefined) {
      return;
    }
    let stopped = false;
    let reading = false;
    const tick = async () => {
      const from = latest.current.held;
      if (reading || from === undefined || document.visibilityState !== "visible") {
        return;
      }
      reading = true;
      try {
        const page = await getLogDay(base.date, from.next);
        if (stopped) {
          return;
        }
        setOffline(false);
        setGrown((current) => ({ base, held: appendPage(current?.base === base ? current.held : base, page) }));
        if (page.entries.length > 0) {
          latest.current.onFresh(page.entries);
        }
        const turned = dayTurned(page);
        if (turned !== undefined) {
          latest.current.onDayTurned(turned);
        }
      } catch {
        if (!stopped) {
          setOffline(true);
        }
      } finally {
        reading = false;
      }
    };
    const timer = window.setInterval(() => void tick(), FOLLOW_INTERVAL);
    const onVisibility = () => void tick();
    document.addEventListener("visibilitychange", onVisibility);
    return () => {
      stopped = true;
      window.clearInterval(timer);
      document.removeEventListener("visibilitychange", onVisibility);
    };
  }, [active, base]);

  return { held, offline: active && offline };
}
