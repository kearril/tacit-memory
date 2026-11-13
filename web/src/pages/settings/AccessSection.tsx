import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useState } from "react";
import { type UseFormRegisterReturn, useForm } from "react-hook-form";
import { ApiError } from "../../api/client";
import { type AccessPasswordBody, clearAccessPassword, SESSION_QUERY_KEY, setAccessPassword } from "../../api/session";
import { useDictionary, useSessionStatus } from "../../app/session-context";
import { errorTextOf } from "../../i18n/messages";
import { Button } from "../../components/ui/button";
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
import { Input } from "../../components/ui/input";
import { Label } from "../../components/ui/label";

interface PasswordValues {
  password: string;
  current: string;
}

interface Notice {
  readonly tone: "success" | "error";
  readonly message: string;
}

/**
 * The access section of the settings page: whether an access password is set, setting or changing it, and clearing it
 * from a local connection. The service's explanations for a refused field are shown beside that field.
 */
export function AccessSection() {
  const dictionary = useDictionary();
  const session = useSessionStatus();
  const queryClient = useQueryClient();
  const [notice, setNotice] = useState<Notice | null>(null);
  const [revealNew, setRevealNew] = useState(false);
  const [revealCurrent, setRevealCurrent] = useState(false);
  const form = useForm<PasswordValues>({ defaultValues: { password: "", current: "" } });
  const { errors } = form.formState;

  const save = useMutation({
    mutationFn: (values: PasswordValues) => setAccessPassword(bodyFor(values, session.local)),
    onSuccess: (status) => {
      queryClient.setQueryData(SESSION_QUERY_KEY, status);
      form.reset({ password: "", current: "" });
      setNotice({ tone: "success", message: dictionary.access.saved });
    },
    onError: (error) => {
      if (error instanceof ApiError && error.error === "PasswordTooShort") {
        form.setError("password", { type: "server", message: error.message });
      } else if (error instanceof ApiError && error.error === "PasswordWrong") {
        form.setError("current", { type: "server", message: error.message });
      } else {
        setNotice({ tone: "error", message: errorTextOf(error, dictionary) });
      }
    },
  });

  const clear = useMutation({
    mutationFn: clearAccessPassword,
    onSuccess: (status) => {
      queryClient.setQueryData(SESSION_QUERY_KEY, status);
      setNotice({ tone: "success", message: dictionary.access.cleared });
    },
    onError: (error) => setNotice({ tone: "error", message: errorTextOf(error, dictionary) }),
  });

  const onSubmit = (values: PasswordValues) => {
    setNotice(null);
    form.clearErrors();
    save.mutate(values);
  };

  return (
    <section aria-labelledby="access-title" className="grid gap-md rounded-lg border-(length:--tk-border-thin) border-line bg-surface p-lg">
      <h2 id="access-title" className="text-lg font-semibold">
        {dictionary.access.title}
      </h2>
      <p className="text-sm text-ink-muted">
        {session.passwordSet ? dictionary.access.passwordSet : dictionary.access.passwordUnset}
      </p>
      <p className="text-sm text-ink-muted">{dictionary.access.note}</p>

      <form className="grid gap-md" onSubmit={form.handleSubmit(onSubmit)} noValidate>
        <PasswordField
          id="access-new-password"
          label={dictionary.access.newPassword}
          registration={form.register("password")}
          error={errors.password?.message}
          revealed={revealNew}
          showLabel={dictionary.showPassword}
          hideLabel={dictionary.hidePassword}
          onToggle={() => setRevealNew((value) => !value)}
        />
        {session.local ? null : (
          <PasswordField
            id="access-current-password"
            label={dictionary.access.currentPassword}
            registration={form.register("current")}
            error={errors.current?.message}
            revealed={revealCurrent}
            showLabel={dictionary.showPassword}
            hideLabel={dictionary.hidePassword}
            onToggle={() => setRevealCurrent((value) => !value)}
          />
        )}
        <div>
          <Button type="submit" disabled={save.isPending}>
            {save.isPending
              ? dictionary.access.saving
              : session.passwordSet
                ? dictionary.access.changePassword
                : dictionary.access.setPassword}
          </Button>
        </div>
      </form>

      {session.local && session.passwordSet ? (
        <AlertDialog>
          <AlertDialogTrigger asChild>
            <Button variant="outline" disabled={clear.isPending}>
              {dictionary.access.clearPassword}
            </Button>
          </AlertDialogTrigger>
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>{dictionary.access.clearTitle}</AlertDialogTitle>
              <AlertDialogDescription>{dictionary.access.clearBody}</AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel>{dictionary.access.cancel}</AlertDialogCancel>
              <AlertDialogAction variant="destructive" onClick={() => clear.mutate()}>
                {dictionary.access.clearConfirm}
              </AlertDialogAction>
            </AlertDialogFooter>
          </AlertDialogContent>
        </AlertDialog>
      ) : null}

      {notice === null ? null : (
        <p
          role={notice.tone === "error" ? "alert" : "status"}
          className={notice.tone === "error" ? "text-sm text-danger" : "text-sm text-success"}
        >
          {notice.message}
        </p>
      )}
    </section>
  );
}

/** The body of a set or change: a remote connection must give the current password, a local one does not. */
function bodyFor(values: PasswordValues, local: boolean): AccessPasswordBody {
  return local ? { password: values.password } : { password: values.password, current: values.current };
}

interface PasswordFieldProps {
  readonly id: string;
  readonly label: string;
  readonly registration: UseFormRegisterReturn;
  readonly error: string | undefined;
  readonly revealed: boolean;
  readonly showLabel: string;
  readonly hideLabel: string;
  readonly onToggle: () => void;
}

/** A password input with its visible label, a show/hide control, and the service's explanation beside it when refused. */
function PasswordField({
  id,
  label,
  registration,
  error,
  revealed,
  showLabel,
  hideLabel,
  onToggle,
}: PasswordFieldProps) {
  const errorId = `${id}-error`;
  return (
    <div className="grid gap-sm">
      <Label htmlFor={id}>{label}</Label>
      <div className="flex gap-sm">
        <Input
          id={id}
          type={revealed ? "text" : "password"}
          autoComplete="off"
          aria-invalid={error !== undefined}
          aria-describedby={error === undefined ? undefined : errorId}
          {...registration}
        />
        <Button type="button" variant="outline" aria-controls={id} aria-pressed={revealed} onClick={onToggle}>
          {revealed ? hideLabel : showLabel}
        </Button>
      </div>
      {error === undefined ? null : (
        <p id={errorId} role="alert" className="text-sm text-danger">
          {error}
        </p>
      )}
    </div>
  );
}
