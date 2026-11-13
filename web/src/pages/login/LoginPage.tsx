import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useReducer, useState } from "react";
import { useForm } from "react-hook-form";
import { useLocation, useNavigate } from "react-router";
import { ApiError } from "../../api/client";
import { login, SESSION_QUERY_KEY } from "../../api/session";
import { Button } from "../../components/ui/button";
import { Input } from "../../components/ui/input";
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
  const [revealed, setRevealed] = useState(false);
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
    <main className="flex min-h-dvh items-center justify-center p-md">
      <section className="grid w-full max-w-container-content gap-md rounded-lg border-(length:--tk-border-thin) border-line bg-surface p-lg">
        <h1 className="text-xl font-semibold">{dictionary.login.title}</h1>
        <p className="text-sm text-ink-muted">{dictionary.login.description}</p>
        <form className="grid gap-md" onSubmit={form.handleSubmit(onSubmit)} noValidate>
          <div className="grid gap-sm">
            <Label htmlFor="login-password">{dictionary.login.passwordLabel}</Label>
            <div className="flex gap-sm">
              <Input
                id="login-password"
                type={revealed ? "text" : "password"}
                autoComplete="current-password"
                aria-invalid={showError}
                aria-describedby={showError ? "login-error" : undefined}
                {...passwordField}
              />
              <Button
                type="button"
                variant="outline"
                aria-controls="login-password"
                aria-pressed={revealed}
                onClick={() => setRevealed((value) => !value)}
              >
                {revealed ? dictionary.hidePassword : dictionary.showPassword}
              </Button>
            </div>
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
      </section>
    </main>
  );
}
