import { useMutation, useQueryClient } from "@tanstack/react-query";
import { CircleAlert, CircleCheck, Link2, Unlink } from "lucide-react";
import { useState } from "react";
import {
  BINDINGS_QUERY_KEY,
  type BindingCheck,
  type BindingDone,
  connectClient,
  type ConnectRequest,
  disconnectClient,
  useBindings,
} from "../../api/bindings";
import { STATUS_QUERY_KEY } from "../../api/status";
import { useDictionary } from "../../app/session-context";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "../../components/ui/alert-dialog";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import { Card, CardDescription, CardTitle } from "../../components/ui/card";
import { CopyButton } from "../../components/ui/copy-button";
import { Input } from "../../components/ui/input";
import { Label } from "../../components/ui/label";
import { useToast } from "../../components/ui/toast-context";
import { errorTextOf } from "../../i18n/messages";
import { BusyIcon } from "../dashboard/parts";
import { complete, EMPTY, presetsLeft } from "./bindings";
import { Explaining } from "./fields";
import { sectionIdOf } from "./form";

/**
 * The bindings card: the bound clients with what is wrong with each and a way to unbind them on the left, and a form to bind
 * another on the right, filled in by hand or from a preset. Binding and unbinding happen at once; they are not changes the
 * page saves. A binding that succeeds shows the MCP server to register in the client, to copy.
 */
export function BindingsCard({ className }: { readonly className?: string }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.bindings;
  const bindings = useBindings();
  const items = bindings.data?.items ?? [];

  return (
    <Card id={sectionIdOf("bindings")} aria-labelledby="bindings-title" className={`scroll-mt-xl ${className ?? ""}`}>
      <header className="flex items-start gap-md">
        <span className="grid size-(--tk-size-control) shrink-0 place-items-center rounded-md bg-accent-soft text-accent">
          <Link2 aria-hidden className="size-(--tk-font-size-lg)" />
        </span>
        <div className="grid min-w-0 flex-1 gap-xs">
          <CardTitle id="bindings-title">{text.title}</CardTitle>
          <CardDescription>{text.summary}</CardDescription>
        </div>
      </header>

      <div className="grid gap-lg lg:grid-cols-2">
        <section aria-label={text.title} className="grid content-start gap-sm">
          {bindings.isError ? (
            <Alert tone="danger">
              <AlertTitle>{dictionary.settings.loadFailed}</AlertTitle>
              <AlertDescription>{errorTextOf(bindings.error, dictionary)}</AlertDescription>
            </Alert>
          ) : bindings.data === undefined ? null : items.length === 0 ? (
            <p className="text-sm text-ink-muted">{text.empty}</p>
          ) : (
            <ul className="grid gap-sm">
              {items.map((binding) => (
                <BindingItem key={binding.client} binding={binding} />
              ))}
            </ul>
          )}
        </section>
        <AddBinding bound={items.map((binding) => binding.client)} />
      </div>
    </Card>
  );
}

/** One bound client: its name and whether it is sound, its two paths, who shares them, and the unbind button. */
function BindingItem({ binding }: { readonly binding: BindingCheck }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.bindings;
  const queries = useQueryClient();
  const showToast = useToast();
  const unbind = useMutation({
    mutationFn: () => disconnectClient(binding.client),
    onSuccess: async (done) => {
      showToast(text.unbound(done.client));
      await Promise.all([
        queries.invalidateQueries({ queryKey: BINDINGS_QUERY_KEY }),
        queries.invalidateQueries({ queryKey: STATUS_QUERY_KEY }),
      ]);
    },
  });

  return (
    <li className="grid min-w-0 gap-xs rounded-lg bg-surface-raised p-md">
      <div className="flex items-center justify-between gap-md">
        <span className="flex min-w-0 flex-wrap items-center gap-sm">
          <span className="font-semibold">{binding.client}</span>
          {binding.problem == null ? (
            <Badge tone="success">
              <CircleCheck aria-hidden />
              {text.healthy}
            </Badge>
          ) : (
            <Badge tone="danger">
              <CircleAlert aria-hidden />
              {dictionary.dashboard.bindingProblems[binding.problem]}
            </Badge>
          )}
        </span>
        <AlertDialog>
          <AlertDialogTrigger asChild>
            <Button type="button" variant="ghost" disabled={unbind.isPending}>
              <BusyIcon busy={unbind.isPending} icon={Unlink} />
              {text.unbind}
            </Button>
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{text.unbindTitle(binding.client)}</AlertDialogTitle>
              <AlertDialogDescription>{text.unbindBody}</AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>{dictionary.settings.cancel}</AlertDialogCancel>
              <AlertDialogAction variant="destructive" onClick={() => unbind.mutate()}>
                {text.unbind}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      </div>
      <dl className="grid gap-x-md gap-y-xs text-xs sm:grid-cols-[auto_minmax(0,1fr)]">
        <dt className="text-ink-muted">{text.fields.contextFile.label}</dt>
        <dd className="font-mono break-all">{binding.contextFile}</dd>
        <dt className="text-ink-muted">{text.fields.skillsFolder.label}</dt>
        <dd className="font-mono break-all">
          {binding.skillsFolder}
          {binding.skillLink == null ? null : <span className="block text-danger">{binding.skillLink}</span>}
        </dd>
      </dl>
      {binding.sharedWith.length === 0 ? null : (
        <p className="text-xs text-ink-muted">{text.sharedWith(binding.sharedWith.join(", "))}</p>
      )}
      {unbind.isError ? (
        <p role="alert" className="text-sm text-danger">
          {text.unbindFailed}：{errorTextOf(unbind.error, dictionary)}
        </p>
      ) : null}
    </li>
  );
}

/** The form that binds a client: the presets not bound yet, the three paths, and what the binding did once it succeeds. */
function AddBinding({ bound }: { readonly bound: readonly string[] }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.bindings;
  const queries = useQueryClient();
  const [request, setRequest] = useState<ConnectRequest>(EMPTY);
  const [done, setDone] = useState<BindingDone | null>(null);
  const bind = useMutation({
    mutationFn: connectClient,
    onMutate: () => setDone(null),
    onSuccess: async (result) => {
      setDone(result);
      setRequest(EMPTY);
      await Promise.all([
        queries.invalidateQueries({ queryKey: BINDINGS_QUERY_KEY }),
        queries.invalidateQueries({ queryKey: STATUS_QUERY_KEY }),
      ]);
    },
  });
  const presets = presetsLeft(bound);
  const fields = ["client", "contextFile", "skillsFolder"] as const;

  return (
    <section aria-labelledby="binding-add" className="grid content-start gap-md lg:border-l-(length:--tk-border-thin) lg:border-line lg:pl-lg">
      <h3 id="binding-add" className="text-sm font-semibold">
        {text.add}
      </h3>
      {presets.length === 0 ? null : (
        <div className="flex flex-wrap items-center gap-sm">
          <span className="text-xs text-ink-muted">{text.presets}</span>
          {presets.map((preset) => (
            <Button
              key={preset.client}
              type="button"
              variant="outline"
             
              aria-pressed={request.client === preset.client}
              onClick={() => setRequest(preset)}
            >
              {preset.client}
            </Button>
          ))}
        </div>
      )}
      <form
        className="grid gap-md"
        onSubmit={(event) => {
          event.preventDefault();
          if (complete(request)) {
            bind.mutate(request);
          }
        }}
      >
        {fields.map((field) => (
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
        <p className="text-xs text-ink-muted">{text.pathsNote}</p>
        {bind.isError ? (
          <Alert tone="danger">
            <AlertTitle>{text.bindFailed}</AlertTitle>
            <AlertDescription>{errorTextOf(bind.error, dictionary)}</AlertDescription>
          </Alert>
        ) : null}
        <div className="flex justify-end">
          <Button type="submit" disabled={!complete(request) || bind.isPending}>
            <BusyIcon busy={bind.isPending} icon={Link2} />
            {text.bind}
          </Button>
        </div>
      </form>
      {done === null ? null : <Bound done={done} onDismiss={() => setDone(null)} />}
    </section>
  );
}

/** What a binding did: the MCP server to register in the client, each value with a copy button, then the summary lines folded away. */
function Bound({ done, onDismiss }: { readonly done: BindingDone; readonly onDismiss: () => void }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.bindings;
  const mcp = done.mcp;
  const rows = mcp == null ? [] : ([["url", mcp.url], ["transport", mcp.transport], ["name", mcp.name]] as const);

  return (
    <Alert tone="success">
      <AlertTitle>{text.bound(done.client)}</AlertTitle>
      <AlertDescription className="grid gap-md">
        {mcp == null ? null : (
          <div className="grid gap-sm">
            <p className="font-medium text-ink">{text.mcpTitle}</p>
            <dl className="grid grid-cols-[auto_minmax(0,1fr)_auto] items-center gap-x-md gap-y-xs">
              {rows.map(([key, value]) => (
                <div key={key} className="contents">
                  <dt className="text-xs text-ink-muted">{text.mcp[key]}</dt>
                  <dd className="font-mono text-sm break-all text-ink">{value}</dd>
                  <CopyButton
                    value={value}
                    label={dictionary.dashboard.copy(text.mcp[key])}
                    copiedMessage={dictionary.dashboard.copied(text.mcp[key])}
                  />
                </div>
              ))}
            </dl>
            <p className="text-xs">{text.mcpNote}</p>
          </div>
        )}
        <details className="text-xs">
          <summary className="w-fit cursor-pointer">{text.details}</summary>
          <ul className="mt-xs grid gap-xs">
            {done.lines.map((line, index) => (
              <li key={index} className="break-all">
                {line}
              </li>
            ))}
          </ul>
        </details>
        <div>
          <Button type="button" variant="outline" onClick={onDismiss}>
            {text.dismiss}
          </Button>
        </div>
      </AlertDescription>
    </Alert>
  );
}
