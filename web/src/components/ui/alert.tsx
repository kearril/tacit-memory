import * as React from "react";
import { cva, type VariantProps } from "class-variance-authority";
import { cn } from "cn";
import { CircleAlert, CircleCheck, Info, TriangleAlert, type LucideIcon } from "lucide-react";

// Structure follows shadcn/ui's alert: a message that stays in place beside what it is about, on a soft wash of its tone,
// with an icon that says the tone so it never rests on colour alone. An error or warning is announced at once (role
// "alert"); a success or a piece of information is announced politely (role "status").
const alertVariants = cva("grid grid-cols-[auto_minmax(0,1fr)] gap-x-sm gap-y-xs rounded-md px-md py-sm text-sm", {
  variants: {
    tone: {
      info: "bg-info-soft text-info",
      success: "bg-success-soft text-success",
      warning: "bg-warning-soft text-warning",
      danger: "bg-danger-soft text-danger",
    },
  },
  defaultVariants: {
    tone: "info",
  },
});

type Tone = NonNullable<VariantProps<typeof alertVariants>["tone"]>;

const icons: Record<Tone, LucideIcon> = {
  info: Info,
  success: CircleCheck,
  warning: TriangleAlert,
  danger: CircleAlert,
};

function Alert({
  className,
  tone = "info",
  children,
  ...props
}: React.ComponentProps<"div"> & VariantProps<typeof alertVariants>) {
  const Icon = icons[tone ?? "info"];
  return (
    <div
      data-slot="alert"
      role={tone === "danger" || tone === "warning" ? "alert" : "status"}
      className={cn(alertVariants({ tone, className }))}
      {...props}
    >
      <Icon aria-hidden className="mt-(--tk-border-thick) size-(--tk-font-size-base)" />
      {children}
    </div>
  );
}

function AlertTitle({ className, ...props }: React.ComponentProps<"p">) {
  return <p data-slot="alert-title" className={cn("font-medium", className)} {...props} />;
}

// A div, as in shadcn/ui, so a description can hold a list as well as a sentence.
function AlertDescription({ className, ...props }: React.ComponentProps<"div">) {
  return <div data-slot="alert-description" className={cn("col-start-2", className)} {...props} />;
}

export { Alert, AlertDescription, AlertTitle };
