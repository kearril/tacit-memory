import { useEffect, useRef } from "react";
import { useBlocker } from "react-router";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "./ui/alert-dialog";

/** What the question before leaving says: its title, the line under it, and the two buttons. */
export interface LeaveText {
  readonly title: string;
  readonly body: string;
  readonly confirm: string;
  readonly cancel: string;
}

/**
 * Keeps unsaved changes from being lost: a move to another address of the interface asks first, and closing or reloading
 * the tab is met with the browser's own question. A move within the same address, such as to a hash, is let through.
 */
export function LeaveGuard({ dirty, text }: { readonly dirty: boolean; readonly text: LeaveText }) {
  // Leaving closes the dialog too; that close must not reset the navigation it just let through.
  const leaving = useRef(false);
  const blocker = useBlocker(
    ({ currentLocation, nextLocation }) => dirty && currentLocation.pathname !== nextLocation.pathname,
  );

  useEffect(() => {
    if (!dirty) {
      return;
    }
    const onBeforeUnload = (event: BeforeUnloadEvent) => event.preventDefault();
    window.addEventListener("beforeunload", onBeforeUnload);
    return () => window.removeEventListener("beforeunload", onBeforeUnload);
  }, [dirty]);

  return (
    <AlertDialog
      open={blocker.state === "blocked"}
      onOpenChange={(open) => {
        if (!open && !leaving.current) {
          blocker.reset?.();
        }
      }}
    >
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>{text.title}</AlertDialogTitle>
          <AlertDialogDescription>{text.body}</AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>{text.cancel}</AlertDialogCancel>
          <AlertDialogAction
            variant="destructive"
            onClick={() => {
              leaving.current = true;
              blocker.proceed?.();
            }}
          >
            {text.confirm}
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  );
}
