import { cn } from "cn";
import {
  Box,
  Check,
  ChevronDown,
  CircleHelp,
  FolderGit2,
  Gavel,
  Hammer,
  type LucideIcon,
  MessageSquareQuote,
  Siren,
} from "lucide-react";
import { useEffect, useState } from "react";
import { Link } from "react-router";
import type { FileProblem } from "../../api/memory";
import { useDictionary } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Badge } from "../../components/ui/badge";
import { CopyButton } from "../../components/ui/copy-button";
import { copyText } from "../../components/ui/copy-text";
import { useToast } from "../../components/ui/toast-context";
import { PAGE_TYPES, type PageType, TOPICS } from "./pages";
import type { SourceView } from "../../api/memory";
import { memoryPathOf } from "./views";

/** How long a code pill shows its check mark after a copy. */
const COPIED_MILLISECONDS = 1500;

/**
 * A short code in a small monospace pill. A click copies it, so it can be handed to an agent that changes or removes what
 * it names; a copy that worked shows a check mark for a moment and a toast. With `reveal`, it shows only while the row it
 * sits in (a `group`) is hovered or holds the focus.
 */
export function CodePill({
  code,
  reveal = false,
  className,
}: {
  readonly code: string;
  readonly reveal?: boolean;
  readonly className?: string;
}) {
  const dictionary = useDictionary();
  const showToast = useToast();
  const [copied, setCopied] = useState(false);

  useEffect(() => {
    if (!copied) {
      return;
    }
    const timer = window.setTimeout(() => setCopied(false), COPIED_MILLISECONDS);
    return () => window.clearTimeout(timer);
  }, [copied]);

  const label = dictionary.memory.parts.code(code);
  return (
    <button
      type="button"
      title={dictionary.dashboard.copy(label)}
      aria-label={dictionary.dashboard.copy(label)}
      onClick={async () => {
        if (await copyText(code)) {
          setCopied(true);
          showToast(dictionary.dashboard.copied(label));
        }
      }}
      className={cn(
        "inline-flex shrink-0 cursor-pointer items-center gap-xs rounded-sm bg-surface-raised px-xs font-mono text-xs text-ink-muted outline-none hover:text-ink focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
        copied && "text-success hover:text-success",
        reveal && "opacity-0 transition-opacity group-focus-within:opacity-100 group-hover:opacity-100",
        className,
      )}
    >
      {code}
      {copied ? <Check aria-hidden className="size-(--tk-font-size-xs)" /> : null}
    </button>
  );
}

/** The button above an entry that opens and folds the user's words kept with it. */
export function QuoteToggle({
  open,
  controls,
  onToggle,
}: {
  readonly open: boolean;
  /** The id of the list it opens. */
  readonly controls: string;
  readonly onToggle: () => void;
}) {
  const text = useDictionary().memory.parts;
  return (
    <button
      type="button"
      aria-expanded={open}
      aria-controls={controls}
      onClick={onToggle}
      className={cn(
        "inline-flex cursor-pointer items-center gap-xs rounded-full px-sm py-xs leading-(--tk-line-height-tight) font-medium transition-colors outline-none hover:bg-accent hover:text-on-accent focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
        open ? "bg-accent text-on-accent" : "bg-accent-soft text-accent",
      )}
    >
      <MessageSquareQuote aria-hidden className="size-(--tk-font-size-sm)" />
      {text.quoteToggle}
      <ChevronDown aria-hidden className={cn("size-(--tk-font-size-sm) transition-transform", open && "rotate-180")} />
    </button>
  );
}

/** The user's words kept with an entry, each behind a bar, with its day when there are several. */
export function QuoteList({ id, sources }: { readonly id: string; readonly sources: readonly SourceView[] }) {
  const quoted = sources.filter((source) => source.quote !== null);
  return (
    <div id={id} className="grid gap-xs">
      {quoted.map((source, index) => (
        <p
          key={index}
          className="border-l-(length:--tk-border-thick) border-line pl-sm text-sm leading-(--tk-line-height-normal) text-ink-muted"
        >
          {quoted.length > 1 && source.date !== null ? (
            <span className="tabular-nums">{`${source.date} · `}</span>
          ) : null}
          {source.quote}
        </p>
      ))}
    </div>
  );
}

/** Where an entry came from, in small muted type on a line of its own: the day, then the user's words when they were kept. */
export function SourceNote({ source, inline = false }: { readonly source: SourceView; readonly inline?: boolean }) {
  const text = useDictionary().memory.parts;
  const Tag = inline ? "span" : "p";
  return (
    <Tag className="block text-xs text-ink-muted">
      {source.date === null ? null : <span className="tabular-nums">{source.date}</span>}
      {source.date !== null && source.quote !== null ? " · " : null}
      {source.quote === null ? null : text.quote(source.quote)}
    </Tag>
  );
}

/** A memory file's full path in monospace, cut short with an ellipsis when it does not fit, and a button that copies it. */
export function FilePath({ path }: { readonly path: string }) {
  const dictionary = useDictionary();
  const label = dictionary.memory.parts.path;
  return (
    <div className="flex min-w-0 items-center gap-xs text-xs text-ink-muted">
      <span title={path} className="min-w-0 truncate font-mono select-all">
        {path}
      </span>
      <CopyButton
        value={path}
        label={dictionary.dashboard.copy(label)}
        copiedMessage={dictionary.dashboard.copied(label)}
      />
    </div>
  );
}

/** Each page type's icon; the types are told apart by icon and name, never by a state colour. */
export const PAGE_TYPE_ICONS: Readonly<Record<PageType, LucideIcon>> = {
  incident: Siren,
  decision: Gavel,
  entity: Box,
  work: Hammer,
};

/** A page's type as a neutral badge with its icon; a type that is not one of the four is shown as written. */
export function PageTypeBadge({ type }: { readonly type: string | null }) {
  const text = useDictionary().memory.parts;
  const known = (PAGE_TYPES as readonly string[]).includes(type ?? "") ? (type as PageType) : null;
  const Icon = known === null ? CircleHelp : PAGE_TYPE_ICONS[known];
  return (
    <Badge>
      <Icon aria-hidden />
      {known === null ? (type ?? text.untyped) : text.pageTypes[known]}
    </Badge>
  );
}

/** A workstream's name in a pill on the soft accent colour, the same for every workstream; it leads to the workstream. */
export function WorkstreamPill({ name }: { readonly name: string }) {
  const text = useDictionary().memory.parts;
  const label = name === TOPICS ? text.topics : name;
  const className =
    "inline-flex shrink-0 items-center gap-xs rounded-full bg-accent-soft px-sm py-xs text-xs leading-(--tk-line-height-tight) font-medium text-accent [&_svg]:size-(--tk-font-size-sm)";
  return name === TOPICS ? (
    <span className={className}>{label}</span>
  ) : (
    <Link
      to={`${memoryPathOf("workstreams")}/${encodeURIComponent(name)}`}
      className={cn(
        className,
        "outline-none hover:underline focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent",
      )}
    >
      <FolderGit2 aria-hidden />
      {label}
    </Link>
  );
}

/** A memory file's problems in a warning, each on a line in the consolidation report's words: where, what kind, what is wrong. */
export function FileProblems({ problems }: { readonly problems: readonly FileProblem[] }) {
  const text = useDictionary().memory.parts;
  if (problems.length === 0) {
    return null;
  }
  return (
    <Alert tone="warning">
      <AlertTitle>{text.problemsTitle(problems.length)}</AlertTitle>
      <AlertDescription>
        <ul className="grid gap-xs">
          {problems.map((problem) => (
            <li key={`${problem.location}-${problem.kind}`}>
              {`${problem.location} · ${problem.kind}: ${problem.description}`}
            </li>
          ))}
        </ul>
      </AlertDescription>
    </Alert>
  );
}
