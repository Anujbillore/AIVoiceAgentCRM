import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { CheckCircle2 } from "lucide-react";

interface SuccessPopupState {
  showSuccess: (message?: string) => void;
}

const SuccessPopupContext = createContext<SuccessPopupState | undefined>(undefined);

export function SuccessPopupProvider({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const [message, setMessage] = useState("Details Submitted");

  const showSuccess = useCallback((next = "Details Submitted") => {
    setMessage(next);
    setOpen(true);
    window.setTimeout(() => setOpen(false), 2200);
  }, []);

  const value = useMemo(() => ({ showSuccess }), [showSuccess]);

  return (
    <SuccessPopupContext.Provider value={value}>
      {children}
      {open && (
        <div className="pointer-events-none fixed inset-0 z-[80] flex items-center justify-center px-4">
          <div className="absolute inset-0 bg-slate-950/35 backdrop-blur-[2px]" />
          <div className="success-popup relative w-full max-w-sm rounded-3xl border border-white/40 bg-gradient-to-br from-white via-teal-50 to-teal-100 px-6 py-8 text-center shadow-[0_25px_60px_rgba(15,118,110,0.35)]">
            <div className="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-full bg-teal-600 text-white shadow-[0_12px_30px_rgba(13,148,136,0.45)]">
              <CheckCircle2 className="h-8 w-8" />
            </div>
            <p className="font-display text-2xl text-teal-950">{message}</p>
            <p className="mt-2 text-sm text-teal-800/80">Your changes were saved successfully.</p>
          </div>
        </div>
      )}
    </SuccessPopupContext.Provider>
  );
}

export function useSuccessPopup() {
  const ctx = useContext(SuccessPopupContext);
  if (!ctx) {
    throw new Error("useSuccessPopup must be used within SuccessPopupProvider");
  }
  return ctx;
}
