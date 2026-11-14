import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ChevronLeft, Link2, PenLine, Plus, Unlink } from "lucide-react";
import { useState } from "react";
import {
  BINDINGS_QUERY_KEY,
  type BindingCheck,
  type BindingDone,
  connectClient,
  type ConnectRequest,
  disconnectClient,
  type McpServer as McpServerInfo,
  useBindings,
} from "../../api/bindings";
import { STATUS_QUERY_KEY } from "../../api/status";
import { useDictionary } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Button } from "../../components/ui/button";
import { CopyButton } from "../../components/ui/copy-button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "../../components/ui/dialog";
import { Input } from "../../components/ui/input";
import { Label } from "../../components/ui/label";
import { useToast } from "../../components/ui/toast-context";
import { errorTextOf } from "../../i18n/messages";
import { AgentLogo, LOGO_TILES } from "../dashboard/AgentLogo";
import { knownAgentOf } from "../dashboard/agents";
import { BusyIcon } from "../dashboard/parts";
import { complete, EMPTY, presetsLeft } from "./bindings";
import { Explaining, SettingsSection } from "./fields";

/** The name a client goes by on the page: its agent's when the interface knows it, else the binding's own. */
function nameOf(client: string): string {
  return knownAgentOf(client)?.label ?? client;
}

/**
 * The bindings section: each bound client a tile of its logo and name, with whether it is sound on the logo's corner; a
 * tile opens the client's details. The last tile adds a binding. Binding and unbinding happen at once; they are not
 * changes the page saves.
 */
export function BindingsSection() {
  const dictionary = useDictionary();
  const text = dictionary.settings.bindings;
  const bindings = useBindings();
  const items = bindings.data?.items ?? [];
  const [adding, setAdding] = useState(false);
  const [shown, setShown] = useState<string | null>(null);

  return (
    <SettingsSection section="bindings" summary={text.summary} immediate>
      {bindings.isError ? (
        <Alert tone="danger">
          <AlertTitle>{dictionary.settings.loadFailed}</AlertTitle>
          <AlertDescription>{errorTextOf(bindings.error, dictionary)}</AlertDescription>
        </Alert>
      ) : bindings.data === undefined ? null : (
        <ul className={LOGO_TILES}>
          {items.map((binding) => (
            <li key={binding.client}>
              <Tile
                label={nameOf(binding.client)}
                title={`${nameOf(binding.client)} · ${binding.problem == null ? text.healthy : dictionary.dashboard.bindingProblems[binding.problem]}`}
                onClick={() => setShown(binding.client)}
              >
                <AgentLogo icon={knownAgentOf(binding.client)?.icon ?? null} large sound={binding.problem == null} />
              </Tile>
            </li>
          ))}
          <li>
            <Tile label={text.addTile} title={text.add} muted onClick={() => setAdding(true)}>
              <span className="grid size-(--tk-size-logo) place-items-center rounded-lg">
                <Plus aria-hidden className="size-(--tk-font-size-xl)" />
              </span>
            </Tile>
          </li>
        </ul>
      )}
      <AddDialog open={adding} onOpenChange={setAdding} bound={items.map((binding) => binding.client)} />
      <Details
        binding={items.find((binding) => binding.client === shown) ?? null}
        mcp={bindings.data?.mcp ?? null}
        onClose={() => setShown(null)}
      />
    </SettingsSection>
  );
}

/**
 * One tile: a logo over a name, the whole of it a button that looks like one: outlined, and on hover it rises and the
 * outline takes the accent colour. The tile that adds a binding has a dashed outline.
 */
function Tile({
  label,
  title,
  muted = false,
  onClick,
  children,
}: {
  readonly label: string;
  readonly title: string;
  readonly muted?: boolean;
  readonly onClick: () => void;
  readonly children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      title={title}
      aria-label={title}
      className={`lift grid w-full cursor-pointer justify-items-center gap-sm rounded-lg border-(length:--tk-border-thin) p-md outline-none hover:border-accent focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
        muted
          ? "border-dashed border-line text-ink-muted hover:text-accent"
          : "border-line"
      }`}
      onClick={onClick}
    >
      {children}
      <span className="max-w-full truncate text-sm">{label}</span>
    </button>
  );
}

type Step = { readonly kind: "choose" } | { readonly kind: "fill"; readonly known: boolean } | { readonly kind: "done"; readonly done: BindingDone };

/**
 * The dialog that adds a binding, in steps: a client to start from (the known ones not bound yet, by logo, or another),
 * then its name and paths, filled in for a known client; once bound, the MCP server to register in the client.
 */
function AddDialog({
  open,
  onOpenChange,
  bound,
}: {
  readonly open: boolean;
  readonly onOpenChange: (open: boolean) => void;
  readonly bound: readonly string[];
}) {
  const dictionary = useDictionary();
  const text = dictionary.settings.bindings;
  const queries = useQueryClient();
  const [step, setStep] = useState<Step>({ kind: "choose" });
  const [request, setRequest] = useState<ConnectRequest>(EMPTY);
  const bind = useMutation({
    mutationFn: connectClient,
    onSuccess: async (done) => {
      setStep({ kind: "done", done });
      await Promise.all([
        queries.invalidateQueries({ queryKey: BINDINGS_QUERY_KEY }),
        queries.invalidateQueries({ queryKey: STATUS_QUERY_KEY }),
      ]);
    },
  });

  const start = (preset: ConnectRequest | null) => {
    bind.reset();
    setRequest(preset ?? EMPTY);
    setStep({ kind: "fill", known: preset !== null });
  };

  const close = (next: boolean) => {
    if (!next) {
      setStep({ kind: "choose" });
      setRequest(EMPTY);
      bind.reset();
    }
    onOpenChange(next);
  };

  const title =
    step.kind === "choose"
      ? text.add
      : step.kind === "done"
        ? text.bound(nameOf(step.done.client))
        : step.known
          ? text.bindTitle(nameOf(request.client))
          : text.otherTitle;

  return (
    <Dialog open={open} onOpenChange={close}>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>{title}</DialogTitle>
          <DialogDescription>{step.kind === "choose" ? text.choose : step.kind === "fill" ? text.pathsNote : text.mcpNote}</DialogDescription>
        </DialogHeader>

        {step.kind === "choose" ? (
          <ul className="grid grid-cols-2 gap-sm sm:grid-cols-4">
            {presetsLeft(bound).map((preset) => (
              <li key={preset.client}>
                <Choice label={nameOf(preset.client)} onClick={() => start(preset)}>
                  <AgentLogo icon={knownAgentOf(preset.client)?.icon ?? null} large />
                </Choice>
              </li>
            ))}
            <li>
              <Choice label={text.other} muted onClick={() => start(null)}>
                <span className="grid size-(--tk-size-logo) place-items-center rounded-lg bg-surface-raised">
                  <PenLine aria-hidden className="size-(--tk-font-size-lg)" />
                </span>
              </Choice>
            </li>
          </ul>
        ) : step.kind === "fill" ? (
          <form
            className="grid gap-md"
            onSubmit={(event) => {
              event.preventDefault();
              if (complete(request)) {
                bind.mutate(request);
              }
            }}
          >
            {(["client", "contextFile", "skillsFolder"] as const).map((field) => (
              <div key={field} className="grid gap-xs">
                <Explaining help={text.fields[field].help}>
                  <Label htmlFor={`binding-${field}`} className="w-fit">
                    {text.fields[field].label}
                  </Label>
                </Explaining>
                <Input
                  id={`binding-${field}`}
                  autoComplete="off"
                  spellCheck={false}
                  className={field === "client" ? "max-w-(--tk-size-tile)" : "font-mono"}
                  value={request[field]}
                  onChange={(event) => setRequest({ ...request, [field]: event.target.value })}
                />
              </div>
            ))}
            {bind.isError ? (
              <Alert tone="danger">
                <AlertTitle>{text.bindFailed}</AlertTitle>
                <AlertDescription>{errorTextOf(bind.error, dictionary)}</AlertDescription>
              </Alert>
            ) : null}
            <div className="flex flex-wrap justify-between gap-sm">
              <Button type="button" variant="ghost" onClick={() => setStep({ kind: "choose" })}>
                <ChevronLeft aria-hidden />
                {text.back}
              </Button>
              <Button type="submit" disabled={!complete(request) || bind.isPending}>
                <BusyIcon busy={bind.isPending} icon={Link2} />
                {text.bind}
              </Button>
            </div>
          </form>
        ) : (
          <div className="grid gap-md">
            {step.done.mcp == null ? null : (
              <McpServer url={step.done.mcp.url} transport={step.done.mcp.transport} name={step.done.mcp.name} />
            )}
            <details className="text-xs text-ink-muted">
              <summary className="w-fit cursor-pointer">{text.details}</summary>
              <ul className="mt-xs grid gap-xs">
                {step.done.lines.map((line, index) => (
                  <li key={index} className="break-all">
                    {line}
                  </li>
                ))}
              </ul>
            </details>
            <div className="flex justify-end">
              <Button type="button" onClick={() => close(false)}>
                {text.done}
              </Button>
            </div>
          </div>
        )}
      </DialogContent>
    </Dialog>
  );
}

/** A client to start a binding from, by logo and name. */
function Choice({
  label,
  muted = false,
  onClick,
  children,
}: {
  readonly label: string;
  readonly muted?: boolean;
  readonly onClick: () => void;
  readonly children: React.ReactNode;
}) {
  return (
    <button
      type="button"
      className={`grid w-full cursor-pointer justify-items-center gap-sm rounded-lg border-(length:--tk-border-thin) border-line p-md text-sm outline-none hover:border-accent focus-visible:ring-(length:--tk-border-thick) focus-visible:ring-accent ${
        muted ? "border-dashed text-ink-muted" : "font-medium"
      }`}
      onClick={onClick}
    >
      {children}
      <span className="max-w-full text-center leading-(--tk-line-height-tight) break-words">{label}</span>
    </button>
  );
}

/**
 * A bound client's details: what is wrong with it, its two paths, who shares them, the MCP server to register in the
 * client, and unbinding, which asks once more in place before it happens.
 */
function Details({
  binding,
  mcp,
  onClose,
}: {
  readonly binding: BindingCheck | null;
  readonly mcp: McpServerInfo | null;
  readonly onClose: () => void;
}) {
  const dictionary = useDictionary();
  const text = dictionary.settings.bindings;
  const queries = useQueryClient();
  const showToast = useToast();
  const [confirming, setConfirming] = useState(false);
  const unbind = useMutation({
    mutationFn: disconnectClient,
    onSuccess: async (done) => {
      showToast(text.unbound(nameOf(done.client)));
      close();
      await Promise.all([
        queries.invalidateQueries({ queryKey: BINDINGS_QUERY_KEY }),
        queries.invalidateQueries({ queryKey: STATUS_QUERY_KEY }),
      ]);
    },
  });

  const close = () => {
    setConfirming(false);
    unbind.reset();
    onClose();
  };

  return (
    <Dialog open={binding !== null} onOpenChange={(open) => (open ? undefined : close())}>
      <DialogContent>
        {binding === null ? null : (
          <>
            <DialogHeader>
              <div className="flex items-center gap-md">
                <AgentLogo icon={knownAgentOf(binding.client)?.icon ?? null} large sound={binding.problem == null} />
                <div className="grid min-w-0 gap-xs">
                  <DialogTitle>{nameOf(binding.client)}</DialogTitle>
                  <DialogDescription>
                    {text.bindingName(binding.client)} · {binding.problem == null ? text.healthy : text.broken}
                  </DialogDescription>
                </div>
              </div>
            </DialogHeader>
            {binding.problem == null ? null : (
              <Alert tone="danger">
                <AlertDescription>{dictionary.dashboard.bindingProblems[binding.problem]}</AlertDescription>
              </Alert>
            )}
            <dl className="grid gap-x-md gap-y-sm text-sm sm:grid-cols-[auto_minmax(0,1fr)]">
              <dt className="text-ink-muted">{text.fields.contextFile.label}</dt>
              <dd className="font-mono text-xs break-all">{binding.contextFile}</dd>
              <dt className="text-ink-muted">{text.fields.skillsFolder.label}</dt>
              <dd className="font-mono text-xs break-all">
                {binding.skillsFolder}
                {binding.skillLink == null ? null : <span className="block text-danger">{binding.skillLink}</span>}
              </dd>
            </dl>
            {binding.sharedWith.length === 0 ? null : (
              <p className="text-xs text-ink-muted">{text.sharedWith(binding.sharedWith.map(nameOf).join(", "))}</p>
            )}
            {mcp === null ? null : <McpServer url={mcp.url} transport={mcp.transport} name={mcp.name} />}
            {unbind.isError ? (
              <p role="alert" className="text-sm text-danger">
                {text.unbindFailed}：{errorTextOf(unbind.error, dictionary)}
              </p>
            ) : null}
            {confirming ? (
              <div className="grid gap-sm rounded-md bg-danger-soft p-md">
                <p className="text-sm font-medium">{text.unbindTitle(nameOf(binding.client))}</p>
                <p className="text-xs text-ink-muted">{text.unbindBody}</p>
                <div className="flex justify-end gap-sm">
                  <Button type="button" variant="outline" onClick={() => setConfirming(false)}>
                    {dictionary.settings.cancel}
                  </Button>
                  <Button type="button" variant="destructive" disabled={unbind.isPending} onClick={() => unbind.mutate(binding.client)}>
                    <BusyIcon busy={unbind.isPending} icon={Unlink} />
                    {text.unbind}
                  </Button>
                </div>
              </div>
            ) : (
              <div className="flex flex-wrap justify-between gap-sm">
                <Button type="button" variant="ghost" className="text-danger" onClick={() => setConfirming(true)}>
                  <Unlink aria-hidden />
                  {text.unbind}
                </Button>
                <Button type="button" variant="outline" onClick={close}>
                  {text.close}
                </Button>
              </div>
            )}
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}

/** The MCP server to register in a client: its address with a copy button, its transport and the suggested name. */
function McpServer({ url, transport, name }: { readonly url: string; readonly transport: string; readonly name: string }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.bindings;
  const rows = [
    ["url", url],
    ["transport", transport],
    ["name", name],
  ] as const;
  return (
    <div className="grid gap-sm rounded-md bg-surface-raised p-md">
      <p className="text-sm font-medium">{text.mcpTitle}</p>
      <dl className="grid grid-cols-[auto_minmax(0,1fr)] items-center gap-x-md">
        {rows.map(([key, value]) => (
          <div key={key} className="contents">
            <dt className="text-xs text-ink-muted">{text.mcp[key]}</dt>
            <dd className="flex min-w-0 items-center gap-xs">
              <span className="min-w-0 font-mono text-sm break-all">{value}</span>
              <CopyButton value={value} label={dictionary.dashboard.copy(text.mcp[key])} copiedMessage={dictionary.dashboard.copied(text.mcp[key])} />
            </dd>
          </div>
        ))}
      </dl>
    </div>
  );
}
