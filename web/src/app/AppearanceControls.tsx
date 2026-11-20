import { Moon, Sun } from "lucide-react";
import { Button } from "../components/ui/button";
import {
  DropdownMenu,
  DropdownMenuCheckboxItem,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuRadioGroup,
  DropdownMenuRadioItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "../components/ui/dropdown-menu";
import { Tooltip, TooltipContent, TooltipTrigger } from "../components/ui/tooltip";
import {
  type Palette,
  PALETTE_NAMES,
  PALETTES,
  originOf,
  setColorScheme,
  setPalette,
  toggleColorScheme,
  useAppearance,
} from "../lib/appearance";
import { useDictionary } from "./session-context";

/**
 * The sidebar's appearance row, above the identity card: a palette button that opens a menu of the palettes and the
 * "follow the system" option, and a light/dark switch that turns to the other scheme from what is on screen.
 */
export function AppearanceControls() {
  const text = useDictionary().appearance;
  const { override, shown, palette } = useAppearance();
  const paletteLabel = text.paletteOf(PALETTE_NAMES[palette]);
  const schemeLabel = shown === "dark" ? text.toLight : text.toDark;

  return (
    <div className="flex items-center gap-xs pl-sm">
      <span className="flex-1 text-xs text-ink-muted">{text.label}</span>
      <DropdownMenu>
        <Tooltip>
          <TooltipTrigger asChild>
            <DropdownMenuTrigger asChild>
              <Button variant="ghost" size="icon" aria-label={paletteLabel}>
                <Swatch palette={palette} className="size-(--tk-font-size-lg)" />
              </Button>
            </DropdownMenuTrigger>
          </TooltipTrigger>
          <TooltipContent side="top">{paletteLabel}</TooltipContent>
        </Tooltip>
        <DropdownMenuContent side="top" align="end" collisionPadding={16} className="w-(--tk-size-list)">
          <DropdownMenuLabel>{text.palette}</DropdownMenuLabel>
          <DropdownMenuRadioGroup value={palette}>
            {PALETTES.map((item) => (
              <DropdownMenuRadioItem
                key={item}
                value={item}
                onSelect={(event) => setPalette(item, originOf(event.currentTarget as Element))}
              >
                <Swatch palette={item} className="size-(--tk-font-size-base)" />
                {PALETTE_NAMES[item]}
              </DropdownMenuRadioItem>
            ))}
          </DropdownMenuRadioGroup>
          <DropdownMenuSeparator />
          <DropdownMenuCheckboxItem
            checked={override === null}
            onSelect={(event) => event.preventDefault()}
            onCheckedChange={(checked) => setColorScheme(checked ? null : shown)}
          >
            {text.followSystem}
          </DropdownMenuCheckboxItem>
        </DropdownMenuContent>
      </DropdownMenu>
      <Tooltip>
        <TooltipTrigger asChild>
          <Button
            variant="ghost"
            size="icon"
            aria-label={schemeLabel}
            onClick={(event) => toggleColorScheme(originOf(event.currentTarget, event))}
          >
            {shown === "dark" ? <Moon aria-hidden /> : <Sun aria-hidden />}
          </Button>
        </TooltipTrigger>
        <TooltipContent side="top">
          {schemeLabel} · {override === null ? text.following : text.pinned}
        </TooltipContent>
      </Tooltip>
    </div>
  );
}

/** A palette's swatch, shown in whichever scheme the page is in. */
function Swatch({ palette, className }: { readonly palette: Palette; readonly className: string }) {
  return <span aria-hidden data-palette={palette} className={`shrink-0 palette-swatch ${className}`} />;
}
