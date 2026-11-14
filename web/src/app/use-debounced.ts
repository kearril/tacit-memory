import { useEffect, useState } from "react";

/** `value` once it has stayed the same for `delay` milliseconds: what the user typed, once they pause. */
export function useDebounced<T>(value: T, delay: number): T {
  const [settled, setSettled] = useState(value);
  useEffect(() => {
    const timer = window.setTimeout(() => setSettled(value), delay);
    return () => window.clearTimeout(timer);
  }, [value, delay]);
  return settled;
}
