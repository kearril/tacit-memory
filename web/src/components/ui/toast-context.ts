import { createContext, useContext } from "react";

/** Shows a short success message as a toast. */
export type ShowToast = (message: string) => void;

export const ToastContext = createContext<ShowToast | undefined>(undefined);

/** The function that shows a toast; read only below the toaster. */
export function useToast(): ShowToast {
  const show = useContext(ToastContext);
  if (show === undefined) {
    throw new Error("A toast is shown only below the toaster.");
  }
  return show;
}
