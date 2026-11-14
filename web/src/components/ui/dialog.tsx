import * as React from "react";
import { cn } from "cn";
import { Dialog as DialogPrimitive } from "radix-ui";

// Structure and behaviour follow shadcn/ui. The colours, sizes, layers and motion are the project's tokens (see
// styles/tokens.css). A dialog holds a task of several steps, such as choosing and previewing; a yes-or-no question is an
// alert dialog instead. It has no close cross: its footer says how to leave.

function Dialog({ ...props }: React.ComponentProps<typeof DialogPrimitive.Root>) {
  return <DialogPrimitive.Root data-slot="dialog" {...props} />;
}

function DialogTrigger({ ...props }: React.ComponentProps<typeof DialogPrimitive.Trigger>) {
  return <DialogPrimitive.Trigger data-slot="dialog-trigger" {...props} />;
}

function DialogClose({ ...props }: React.ComponentProps<typeof DialogPrimitive.Close>) {
  return <DialogPrimitive.Close data-slot="dialog-close" {...props} />;
}

/**
 * The dialog's panel over a scrim. With `locked`, neither the Escape key nor a click on the scrim closes it, for while the
 * task it started cannot be left.
 */
function DialogContent({
  className,
  locked = false,
  onEscapeKeyDown,
  onPointerDownOutside,
  ...props
}: React.ComponentProps<typeof DialogPrimitive.Content> & { locked?: boolean }) {
  return (
    <DialogPrimitive.Portal>
      <DialogPrimitive.Overlay data-slot="dialog-overlay" className="fixed inset-0 z-(--tk-layer-overlay) bg-scrim" />
      <DialogPrimitive.Content
        data-slot="dialog-content"
        className={cn(
          "fixed inset-x-md top-1/2 z-(--tk-layer-dialog) mx-auto grid max-h-[calc(100dvh-2*var(--tk-space-xl))] max-w-content -translate-y-1/2 content-start gap-md overflow-y-auto rounded-lg bg-surface p-lg text-ink shadow-raised",
          className,
        )}
        onEscapeKeyDown={(event) => {
          if (locked) {
            event.preventDefault();
          }
          onEscapeKeyDown?.(event);
        }}
        onPointerDownOutside={(event) => {
          if (locked) {
            event.preventDefault();
          }
          onPointerDownOutside?.(event);
        }}
        {...props}
      />
    </DialogPrimitive.Portal>
  );
}

function DialogHeader({ className, ...props }: React.ComponentProps<"div">) {
  return <div data-slot="dialog-header" className={cn("grid gap-sm", className)} {...props} />;
}

function DialogFooter({ className, ...props }: React.ComponentProps<"div">) {
  return (
    <div
      data-slot="dialog-footer"
      className={cn("flex flex-col-reverse gap-sm pt-sm sm:flex-row sm:justify-end", className)}
      {...props}
    />
  );
}

function DialogTitle({ className, ...props }: React.ComponentProps<typeof DialogPrimitive.Title>) {
  return (
    <DialogPrimitive.Title
      data-slot="dialog-title"
      className={cn("text-lg leading-(--tk-line-height-tight) font-semibold", className)}
      {...props}
    />
  );
}

function DialogDescription({ className, ...props }: React.ComponentProps<typeof DialogPrimitive.Description>) {
  return (
    <DialogPrimitive.Description
      data-slot="dialog-description"
      className={cn("text-sm text-ink-muted", className)}
      {...props}
    />
  );
}

export {
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
};
