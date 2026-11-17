import { useMutation, useQueryClient } from "@tanstack/react-query";
import { ShieldCheck, ShieldOff } from "lucide-react";
import { useState } from "react";
import { type UseFormRegisterReturn, useForm } from "react-hook-form";
import { useStatus } from "../../api/status";
import { type AccessPasswordBody, clearAccessPassword, SESSION_QUERY_KEY, setAccessPassword } from "../../api/session";
import { classifyFailure } from "../../components/command-failure";
import { useDictionary, useSessionStatus } from "../../app/session-context";
import { errorTextOf } from "../../i18n/messages";
import { Alert, AlertDescription, AlertTitle } from "../../components/ui/alert";
import { Badge } from "../../components/ui/badge";
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
import { Label } from "../../components/ui/label";
import { PasswordInput } from "../../components/ui/password-input";
import { useToast } from "../../components/ui/toast-context";
import { Tooltip, TooltipContent, TooltipTrigger } from "../../components/ui/tooltip";
import { Immediate, Setting, SettingRow, SettingsGroup, type SettingsForm, SettingsSection } from "./fields";

interface PasswordValues {
  password: string;
  current: string;
}

/** What the card shows in an alert: a failure the service gave, or why the page did not ask it. */
interface Notice {
  readonly title: string;
  readonly message: string;
}

/**
 * The remote access section of the settings page: first the access password (whether one is set, setting or changing it,
 * and clearing it from a local connection), which takes effect at once; then the extra listener, a setting saved
 * with the rest of the page, which needs the password first. The service's explanations for a refused field are shown beside that field; a success is shown
 * as a toast, and any other failure stays in the card as an alert. While an extra listener is configured the clear button
 * is unavailable: hovering or focusing it explains why, and clicking it (the only way on a touch screen) shows the same
 * explanation as an alert.
 */
export function RemoteAccessSection({ form: settings }: { readonly form: SettingsForm }) {
  const dictionary = useDictionary();
  const text = dictionary.settings.access;
  const session = useSessionStatus();
  const queryClient = useQueryClient();
  const showToast = useToast();
  // An extra listener keeps the password from being cleared; the button says so before the service has to refuse.
  const blockingAddress = useStatus().data?.service?.extraListener?.address ?? null;
  const [failure, setFailure] = useState<Notice | null>(null);
  const form = useForm<PasswordValues>({ defaultValues: { password: "", current: "" } });
  const { errors } = form.formState;

  const save = useMutation({
    mutationFn: (values: PasswordValues) => setAccessPassword(bodyFor(values, session.local)),
    onSuccess: (status) => {
      queryClient.setQueryData(SESSION_QUERY_KEY, status);
      showToast(dictionary.access.saved);
    },
    onError: (error) => {
      // A password too short or a current one that is wrong is the field's own problem, shown beside it.
      const failed = classifyFailure(error);
      if (failed.kind === "failed" && failed.error.error === "PasswordTooShort") {
        form.setError("password", { type: "server", message: failed.error.message });
      } else if (failed.kind === "failed" && failed.error.error === "PasswordWrong") {
        form.setError("current", { type: "server", message: failed.error.message });
      } else {
        setFailure({ title: dictionary.access.saveFailed, message: errorTextOf(failed.error, dictionary) });
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
    <SettingsSection section="access" summary={text.summary}>
      <SettingsGroup
        title={text.password}
        aside={
          <>
            <Immediate />
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
          </>
        }
      >
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
          <SettingRow
            name={null}
            control={
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
                          setFailure({
                            title: dictionary.access.clearFailed,
                            message: dictionary.access.clearBlocked(blockingAddress),
                          })
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
            }
          />
        </form>

        {failure === null ? null : (
          <Alert tone="danger">
            <AlertTitle>{failure.title}</AlertTitle>
            <AlertDescription>{failure.message}</AlertDescription>
          </Alert>
        )}
      </SettingsGroup>

      <SettingsGroup title={text.listener}>
        <Setting
          form={settings}
          path="server.host"
          text={text.host}
          placeholder={text.hostDefault}
          disabled={!session.passwordSet && (settings.drafts["server.host"] ?? "") === ""}
        >
          {session.passwordSet ? null : <p className="text-xs text-ink-muted">{text.hostNeedsPassword}</p>}
        </Setting>
      </SettingsGroup>
    </SettingsSection>
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

/** A password input as a row, its name on the left, the eye toggle inside it, and the service's explanation under it when refused. */
function PasswordField({ id, label, placeholder, registration, error }: PasswordFieldProps) {
  const dictionary = useDictionary();
  const errorId = `${id}-error`;
  return (
    <SettingRow
      name={<Label htmlFor={id}>{label}</Label>}
      control={
        <div className="w-(--tk-size-tile) max-w-full">
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
        </div>
      }
    >
      {error === undefined ? null : (
        <p id={errorId} role="alert" className="text-sm text-danger">
          {error}
        </p>
      )}
    </SettingRow>
  );
}
