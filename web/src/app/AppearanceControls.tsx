import githubIcon from "@lobehub/icons-static-svg/icons/github.svg?url";
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
import { REPOSITORY_URL, useUpdateCheck } from "../lib/update-check";
import { useDictionary, useSessionStatus } from "./session-context";

/**
 * The sidebar's appearance row, above the identity card: a palette button that opens a menu of the palettes and the
 * "follow the system" option, and a light/dark switch that turns to the other scheme from what is on screen.
 */
export function AppearanceControls() {
  const text = useDictionary().appearance;
  const { version } = useSessionStatus();
  const { available } = useUpdateCheck(version);
  const { override, shown, palette } = useAppearance();
  const paletteLabel = text.paletteOf(PALETTE_NAMES[palette]);
  const schemeLabel = shown === "dark" ? text.toLight : text.toDark;
  const githubLabel = available === null ? text.github : text.updateAvailable(available.version);

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
            onSelect={(event) => {
              // Stays open, so the check can be seen to change.
              event.preventDefault();
              setColorScheme(override === null ? shown : null, originOf(event.currentTarget as Element));
            }}
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
      <Tooltip>
        <TooltipTrigger asChild>
          <Button variant="ghost" size="icon" className="relative" asChild>
            <a
              href={available === null ? REPOSITORY_URL : available.url}
              target="_blank"
              rel="noopener noreferrer"
              aria-label={githubLabel}
            >
              <span
                aria-hidden
                className="size-(--tk-font-size-lg) bg-current"
                style={{ mask: `url("${githubIcon}") center / contain no-repeat` }}
              />
              {available === null ? null : (
                <span aria-hidden className="top-1.5 right-1.5 size-1.5 absolute rounded-full bg-accent" />
              )}
            </a>
          </Button>
        </TooltipTrigger>
        <TooltipContent side="top">{githubLabel}</TooltipContent>
      </Tooltip>
    </div>
  );
}

/** A palette's swatch, shown in whichever scheme the page is in. */
function Swatch({ palette, className }: { readonly palette: Palette; readonly className: string }) {
  return <span aria-hidden data-palette={palette} className={`shrink-0 palette-swatch ${className}`} />;
}
