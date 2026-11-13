import { useMutation, useQueryClient } from "@tanstack/react-query";
import { KeyRound, ShieldCheck, ShieldOff } from "lucide-react";
import { useState } from "react";
import { type UseFormRegisterReturn, useForm } from "react-hook-form";
import { ApiError } from "../../api/client";
import { useStatus } from "../../api/status";
import { type AccessPasswordBody, clearAccessPassword, SESSION_QUERY_KEY, setAccessPassword } from "../../api/session";
import { useDictionary, useSessionStatus } from "../../app/session-context";
import { errorTextOf } from "../../i18n/messages";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Badge } from "../../components/ui/badge";
import { Button } from "../../components/ui/button";
import { Card, CardDescription, CardTitle } from "../../components/ui/card";
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
import { Label } from "../../components/ui/label";
import { PasswordInput } from "../../components/ui/password-input";
import { useToast } from "../../components/ui/toast-context";
import { Tooltip, TooltipContent, TooltipTrigger } from "../../components/ui/tooltip";

interface PasswordValues {
  password: string;
  current: string;
}

interface Failure {
  readonly title: string;
  readonly message: string;
}

/**
 * The access section of the settings page: whether an access password is set, setting or changing it, and clearing it
 * from a local connection. The service's explanations for a refused field are shown beside that field; a success is shown
 * as a toast, and any other failure stays in the card as an alert. While an extra listener is configured the clear button
 * is unavailable: hovering or focusing it explains why, and clicking it (the only way on a touch screen) shows the same
 * explanation as an alert.
 */
export function AccessSection({ id, className }: { readonly id?: string; readonly className?: string }) {
  const dictionary = useDictionary();
  const session = useSessionStatus();
  const queryClient = useQueryClient();
  const showToast = useToast();
  // An extra listener keeps the password from being cleared; the button says so before the service has to refuse.
  const blockingAddress = useStatus().data?.service?.extraListener?.address ?? null;
  const [failure, setFailure] = useState<Failure | null>(null);
  const form = useForm<PasswordValues>({ defaultValues: { password: "", current: "" } });
  const { errors } = form.formState;

  const save = useMutation({
    mutationFn: (values: PasswordValues) => setAccessPassword(bodyFor(values, session.local)),
    onSuccess: (status) => {
      queryClient.setQueryData(SESSION_QUERY_KEY, status);
      showToast(dictionary.access.saved);
    },
    onError: (error) => {
      if (error instanceof ApiError && error.error === "PasswordTooShort") {
        form.setError("password", { type: "server", message: error.message });
      } else if (error instanceof ApiError && error.error === "PasswordWrong") {
        form.setError("current", { type: "server", message: error.message });
      } else {
        setFailure({ title: dictionary.access.saveFailed, message: errorTextOf(error, dictionary) });
      }
    },
  });

  const clear = useMutation({
    mutationFn: clearAccessPassword,
    onSuccess: (status) => {
      queryClient.setQueryData(SESSION_QUERY_KEY, status);
      showToast(dictionary.access.cleared);
    },
    onMutate: () => setFailure(null),
    onError: (error) => setFailure({ title: dictionary.access.clearFailed, message: errorTextOf(error, dictionary) }),
  });

  const onSubmit = (values: PasswordValues) => {
    setFailure(null);
    form.clearErrors();
    save.mutate(values);
  };

  return (
    <Card id={id} aria-labelledby="access-title" className={className}>
      <header className="flex items-start gap-md">
        <span className="grid size-(--tk-size-control) shrink-0 place-items-center rounded-md bg-accent-soft text-accent">
          <KeyRound aria-hidden className="size-(--tk-font-size-lg)" />
        </span>
        <div className="grid min-w-0 flex-1 gap-xs">
          <CardTitle id="access-title">{dictionary.access.title}</CardTitle>
          <CardDescription>{dictionary.access.summary}</CardDescription>
        </div>
        {session.passwordSet ? (
          <Badge tone="success">
            <ShieldCheck aria-hidden />
            {dictionary.access.stateSet}
          </Badge>
        ) : (
          <Badge>
            <ShieldOff aria-hidden />
            {dictionary.access.stateUnset}
          </Badge>
        )}
      </header>

      <form className="grid gap-md" onSubmit={form.handleSubmit(onSubmit)} noValidate>
        {session.local ? null : (
          <PasswordField
            id="access-current-password"
            label={dictionary.access.currentPassword}
            placeholder={dictionary.access.currentPlaceholder}
            registration={form.register("current")}
            error={errors.current?.message}
          />
        )}
        <PasswordField
          id="access-new-password"
          label={dictionary.access.newPassword}
          placeholder={dictionary.access.newPlaceholder}
          registration={form.register("password")}
          error={errors.password?.message}
        />
        <div className="flex flex-wrap gap-sm">
          <Button type="submit" disabled={save.isPending}>
            {save.isPending
              ? dictionary.access.saving
              : session.passwordSet
                ? dictionary.access.changePassword
                : dictionary.access.setPassword}
          </Button>
          {session.local && session.passwordSet && blockingAddress !== null ? (
            <Tooltip>
              <TooltipTrigger asChild>
                <Button
                  type="button"
                  variant="outline"
                  aria-disabled="true"
                  onClick={() =>
                    setFailure({ title: dictionary.access.clearFailed, message: dictionary.access.clearBlocked(blockingAddress) })
                  }
                >
                  {dictionary.access.clearPassword}
                </Button>
              </TooltipTrigger>
              <TooltipContent>{dictionary.access.clearBlocked(blockingAddress)}</TooltipContent>
            </Tooltip>
          ) : null}
          {session.local && session.passwordSet && blockingAddress === null ? (
            <AlertDialog>
              <AlertDialogTrigger asChild>
                <Button type="button" variant="outline" disabled={clear.isPending}>
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
        </div>
      </form>

      {failure === null ? null : (
        <Alert tone="danger">
          <AlertTitle>{failure.title}</AlertTitle>
          <AlertDescription>{failure.message}</AlertDescription>
        </Alert>
      )}

    </Card>
  );
}

/** The body of a set or change: a remote connection must give the current password, a local one does not. */
function bodyFor(values: PasswordValues, local: boolean): AccessPasswordBody {
  return local ? { password: values.password } : { password: values.password, current: values.current };
}

interface PasswordFieldProps {
  readonly id: string;
  readonly label: string;
  readonly placeholder: string;
  readonly registration: UseFormRegisterReturn;
  readonly error: string | undefined;
}

/** A password input with its visible label, the eye toggle inside it, and the service's explanation beside it when refused. */
function PasswordField({ id, label, placeholder, registration, error }: PasswordFieldProps) {
  const dictionary = useDictionary();
  const errorId = `${id}-error`;
  return (
    <div className="grid gap-sm">
      <Label htmlFor={id}>{label}</Label>
      <PasswordInput
        id={id}
        autoComplete="off"
        placeholder={placeholder}
        showLabel={dictionary.showPassword}
        hideLabel={dictionary.hidePassword}
        aria-invalid={error !== undefined}
        aria-describedby={error === undefined ? undefined : errorId}
        {...registration}
      />
      {error === undefined ? null : (
        <p id={errorId} role="alert" className="text-sm text-danger">
          {error}
        </p>
      )}
    </div>
  );
}
