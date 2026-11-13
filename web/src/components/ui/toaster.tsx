import * as React from "react";
import { CircleCheck, X } from "lucide-react";
import { Toast as ToastPrimitive } from "radix-ui";
import { ToastContext } from "./toast-context";

// How long a toast stays before it leaves by itself. Hovering or focusing it pauses the count.
const TOAST_DURATION = 5000;

interface ToastItem {
  readonly id: number;
  readonly message: string;
}

// Structure and behaviour follow Radix's toast: a polite announcement in the lower right corner that leaves by itself, can
// be swiped away to the right or closed, and is reached from the keyboard with F8. Toasts carry successes only; a failure
// stays in place as an alert beside what failed.
function Toaster({
  closeLabel,
  viewportLabel,
  children,
}: {
  closeLabel: string;
  viewportLabel: string;
  children: React.ReactNode;
}) {
  const [toasts, setToasts] = React.useState<readonly ToastItem[]>([]);
  const nextId = React.useRef(0);

  const show = React.useCallback((message: string) => {
    const id = nextId.current++;
    setToasts((current) => [...current, { id, message }]);
  }, []);

  return (
    <ToastPrimitive.Provider duration={TOAST_DURATION} swipeDirection="right" label={viewportLabel}>
      <ToastContext.Provider value={show}>{children}</ToastContext.Provider>
      {toasts.map((toast) => (
        <ToastPrimitive.Root
          key={toast.id}
          type="background"
          onOpenChange={(open) => {
            if (!open) {
              setToasts((current) => current.filter((item) => item.id !== toast.id));
            }
          }}
          className="flex items-start gap-sm rounded-md bg-surface p-md text-sm text-ink shadow-raised data-[swipe=move]:translate-x-(--radix-toast-swipe-move-x)"
        >
          <CircleCheck aria-hidden className="mt-(--tk-border-thick) size-(--tk-font-size-base) shrink-0 text-success" />
          <ToastPrimitive.Description className="flex-1">{toast.message}</ToastPrimitive.Description>
          <ToastPrimitive.Close
            aria-label={closeLabel}
            className="grid cursor-pointer place-items-center rounded-sm text-ink-muted transition-colors outline-none hover:text-ink focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent"
          >
            <X aria-hidden className="size-(--tk-font-size-base)" />
          </ToastPrimitive.Close>
        </ToastPrimitive.Root>
      ))}
      <ToastPrimitive.Viewport className="fixed right-0 bottom-0 z-(--tk-layer-toast) flex w-full max-w-content flex-col gap-sm p-md outline-none" />
    </ToastPrimitive.Provider>
  );
}

export { Toaster };
