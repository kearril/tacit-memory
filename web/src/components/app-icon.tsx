import { cn } from "cn";
import appIcon from "../assets/app-icon.svg";

// The product's icon, two hooks linked in a pinky promise, a control high. It heads the sidebar and the login card; the
// favicon and the executable's icon are drawn from the same file.
function AppIcon({ className }: { readonly className?: string }) {
  return <img src={appIcon} alt="" className={cn("size-(--tk-size-control) shrink-0", className)} />;
}

export { AppIcon };
