import { cn } from "cn";
import appIcon from "../assets/app-icon.png";

// The product's icon, layered Markdown cards with a folded corner tab, a control high. It heads the sidebar and the
// login card; the favicon and the executable's icon are generated from the same artwork.
function AppIcon({ className }: { readonly className?: string }) {
  return <img src={appIcon} alt="" className={cn("size-(--tk-size-control) shrink-0", className)} />;
}

export { AppIcon };
