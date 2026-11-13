import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useReducer } from "react";
import { useForm } from "react-hook-form";
import { useLocation, useNavigate } from "react-router";
import { ApiError } from "../../api/client";
import { login, SESSION_QUERY_KEY } from "../../api/session";
import { Brain } from "lucide-react";
import { Button } from "../../components/ui/button";
import { Card, CardDescription, CardTitle } from "../../components/ui/card";
import { PasswordInput } from "../../components/ui/password-input";
import { Label } from "../../components/ui/label";
import { destinationAfterLogin } from "../../app/return-target";
import { useDictionary } from "../../app/session-context";
import { errorTextOf } from "../../i18n/messages";
import { canSubmitLogin, initialLoginAttempt, loginAttemptReducer } from "./login-attempt";

interface LoginValues {
  password: string;
}

/** The login page: one password field that can be shown or hidden, and the submit. Reached only by a remote connection. */
export function LoginPage() {
  const dictionary = useDictionary();
  const location = useLocation();
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const [attempt, dispatch] = useReducer(loginAttemptReducer, initialLoginAttempt);
  const form = useForm<LoginValues>({ defaultValues: { password: "" } });

  const submit = useMutation({
    mutationFn: (password: string) => login(password),
    onMutate: () => dispatch({ type: "submitted" }),
    onSuccess: (status) => {
      dispatch({ type: "succeeded" });
      queryClient.setQueryData(SESSION_QUERY_KEY, status);
      navigate(destinationAfterLogin(location.state), { replace: true });
    },
    onError: (error) =>
      dispatch({
        type: "failed",
        error: { status: error instanceof ApiError ? error.status : 0, message: errorTextOf(error, dictionary) },
      }),
  });

  const passwordField = form.register("password", {
    onChange: () => dispatch({ type: "edited" }),
  });

  const onSubmit = (values: LoginValues) => {
    if (canSubmitLogin(attempt)) {
      submit.mutate(values.password);
    }
  };

  const showError = attempt.phase === "failed" || attempt.phase === "limited";

  return (
    <main className="flex min-h-dvh items-center justify-center bg-canvas p-md">
      <Card className="w-full max-w-content gap-lg p-xl">
        <div className="grid justify-items-start gap-md">
          <span className="grid size-(--tk-size-control) place-items-center rounded-md bg-accent text-on-accent shadow-pressed">
            <Brain aria-hidden className="size-(--tk-font-size-lg)" />
          </span>
          <div className="grid gap-xs">
            <CardTitle className="text-xl">{dictionary.login.title}</CardTitle>
            <CardDescription>{dictionary.login.description}</CardDescription>
          </div>
        </div>
        <form className="grid gap-md" onSubmit={form.handleSubmit(onSubmit)} noValidate>
          <div className="grid gap-sm">
            <Label htmlFor="login-password">{dictionary.login.passwordLabel}</Label>
            <PasswordInput
              id="login-password"
              autoComplete="current-password"
              placeholder={dictionary.login.passwordPlaceholder}
              showLabel={dictionary.showPassword}
              hideLabel={dictionary.hidePassword}
              aria-invalid={showError}
              aria-describedby={showError ? "login-error" : undefined}
              {...passwordField}
            />
          </div>
          {showError ? (
            <p id="login-error" role="alert" className="text-sm text-danger">
              {attempt.message}
            </p>
          ) : null}
          <Button type="submit" disabled={!canSubmitLogin(attempt)}>
            {attempt.phase === "pending" ? dictionary.login.submitting : dictionary.login.submit}
          </Button>
        </form>
      </Card>
    </main>
  );
}
