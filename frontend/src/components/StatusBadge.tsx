import { displayStatus } from "../lib/format";

const styles: Record<string, string> = {
  confirmed: "bg-emerald-50 text-emerald-800",
  scheduled: "bg-emerald-50 text-emerald-800",
  pending: "bg-amber-50 text-amber-800",
  completed: "bg-teal-50 text-teal-800",
  cancelled: "bg-rose-50 text-rose-800",
  canceled: "bg-rose-50 text-rose-800",
  "no show": "bg-slate-100 text-slate-700",
  open: "bg-sky-50 text-sky-800",
  "in progress": "bg-amber-50 text-amber-800",
  resolved: "bg-teal-50 text-teal-800",
  queued: "bg-amber-50 text-amber-800",
  required: "bg-amber-50 text-amber-800",
  fine: "bg-teal-50 text-teal-800",
};

export function StatusBadge({ status }: { status: string }) {
  const key = displayStatus(status);
  return (
    <span className={`inline-flex rounded-full px-2.5 py-1 text-xs font-semibold capitalize ${styles[key] ?? "bg-slate-100 text-slate-700"}`}>
      {key}
    </span>
  );
}
