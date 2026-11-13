import type { BindingProblem, StatusReport } from "../../api/status";

/** One thing the user has to deal with, as the status report shows it. */
export type Todo =
  | { readonly kind: "modelUnconfigured"; readonly role: string }
  | { readonly kind: "noBindings" }
  | { readonly kind: "bindingProblem"; readonly client: string; readonly problem: BindingProblem }
  | { readonly kind: "extraListenerFailed"; readonly address: string; readonly reason: string }
  | { readonly kind: "cloudBackupFailed"; readonly at: string; readonly error: string | null };

/**
 * The things the user has to deal with, in the order the dashboard lists them: each auxiliary model that is not
 * configured, a missing binding or each binding with a problem, an extra listener that failed, and a failed last cloud
 * backup. A cooling model and pending candidates or format problems are not listed: they pass by themselves or are left to
 * consolidation. A column the report leaves out (it could not be read) adds nothing.
 */
export function todosOf(report: StatusReport): Todo[] {
  const todos: Todo[] = [];

  for (const model of report.models?.items ?? []) {
    if (model.state === "unconfigured") {
      todos.push({ kind: "modelUnconfigured", role: model.role });
    }
  }

  const bindings = report.bindings?.items;
  if (bindings !== undefined && bindings.length === 0) {
    todos.push({ kind: "noBindings" });
  }
  for (const binding of bindings ?? []) {
    if (binding.problem !== null) {
      todos.push({ kind: "bindingProblem", client: binding.client, problem: binding.problem });
    }
  }

  const extra = report.service?.extraListener;
  if (extra?.failureReason != null) {
    todos.push({ kind: "extraListenerFailed", address: extra.address, reason: extra.failureReason });
  }

  const cloud = report.backups?.lastRemote;
  if (cloud != null && !cloud.succeeded) {
    todos.push({ kind: "cloudBackupFailed", at: cloud.at, error: cloud.error });
  }

  return todos;
}

/** Where a to-do leads: the settings section that fixes it, or the backup card on the dashboard. */
export function targetOf(todo: Todo): string {
  switch (todo.kind) {
    case "modelUnconfigured":
      return "/settings#models";
    case "noBindings":
    case "bindingProblem":
      return "/settings#bindings";
    case "extraListenerFailed":
      return "/settings#general";
    case "cloudBackupFailed":
      return "/dashboard#backups";
  }
}
