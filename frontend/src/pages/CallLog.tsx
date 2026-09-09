import { useEffect, useMemo, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { api, TOKEN_KEY, type CallLog, type DashboardStats } from "../api/client";
import { StatusBadge } from "../components/StatusBadge";
import { formatStamp, isoDate } from "../lib/format";

export function CallLogPage() {
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [query, setQuery] = useState("");
  const [busy, setBusy] = useState(false);

  async function load() {
    const today = isoDate(new Date());
    const from = isoDate(new Date(Date.now() - 29 * 24 * 60 * 60 * 1000));
    const { data } = await api.get<DashboardStats>("/dashboard", { params: { from, to: today, page: 1, pageSize: 50 } });
    setStats(data);
    setSelectedId((current) => current ?? data.actionItems[0]?.id ?? null);
  }

  useEffect(() => {
    void load();
    const token = localStorage.getItem(TOKEN_KEY);
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/dashboard", { accessTokenFactory: () => token ?? "" })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();
    connection.on("CallSummaryAdded", () => void load());
    connection.on("CallbackQueued", () => void load());
    void connection.start();
    return () => {
      void connection.stop();
    };
  }, []);

  const items = useMemo(() => {
    const term = query.trim().toLowerCase();
    return (stats?.actionItems ?? []).filter(
      (item) =>
        !term ||
        item.callerName.toLowerCase().includes(term) ||
        item.callerPhone.toLowerCase().includes(term) ||
        item.summary.toLowerCase().includes(term) ||
        (item.bookedDoctorName ?? "").toLowerCase().includes(term),
    );
  }, [stats, query]);

  const selected = items.find((item) => item.id === selectedId) ?? items[0] ?? null;

  async function finishCallback(id: number) {
    setBusy(true);
    try {
      await api.post(`/dashboard/calls/${id}/callback/complete`);
      await load();
    } finally {
      setBusy(false);
    }
  }

  if (!stats) return <p className="text-slate-500">Loading call log…</p>;

  return (
    <div className="grid gap-4 xl:grid-cols-[340px_1fr]">
      <section className="card overflow-hidden">
        <div className="border-b border-slate-100 p-4">
          <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search caller, number, doctor..." />
        </div>
        <div className="max-h-[70vh] overflow-y-auto">
          {items.length === 0 ? (
            <p className="px-4 py-8 text-sm text-slate-500">No calls in the last 30 days.</p>
          ) : (
            items.map((item) => (
              <button
                key={item.id}
                onClick={() => setSelectedId(item.id)}
                className={`w-full border-b border-slate-100 px-4 py-3 text-left ${selected?.id === item.id ? "bg-teal-50" : "hover:bg-slate-50"}`}
              >
                <div className="flex items-center justify-between gap-2">
                  <p className="font-medium text-slate-800">{item.callerName}</p>
                  <StatusBadge status={item.callbackStatus || (item.bookedDoctorName ? "confirmed" : "open")} />
                </div>
                <p className="mt-1 text-xs text-slate-500">{item.callerPhone || "No number"} · {formatStamp(item.timestamp)}</p>
                <p className="mt-1 line-clamp-2 text-sm text-slate-600">{item.summary}</p>
              </button>
            ))
          )}
        </div>
      </section>

      <section className="card p-5">
        {!selected ? (
          <p className="text-slate-500">Select a call to see the workflow.</p>
        ) : (
          <CallDetail item={selected} busy={busy} onDone={() => void finishCallback(selected.id)} />
        )}
      </section>
    </div>
  );
}

function CallDetail({ item, busy, onDone }: { item: CallLog; busy: boolean; onDone: () => void }) {
  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <p className="text-xs uppercase tracking-[0.16em] text-teal-700">Call workflow</p>
          <h2 className="font-display text-2xl text-slate-900">{item.callerName}</h2>
          <p className="text-sm text-slate-500">{formatStamp(item.timestamp)}</p>
        </div>
        <StatusBadge status={item.needsPersonalContact ? item.callbackStatus || "required" : "fine"} />
      </div>
      <div className="grid gap-3 sm:grid-cols-2">
        <Field label="Who called" value={item.callerName} />
        <Field label="Contact number" value={item.callerPhone || "—"} />
        <Field label="Booked with" value={item.bookedDoctorName || "Not booked"} />
        <Field
          label="Appointment time"
          value={item.appointmentTime ? formatStamp(item.appointmentTime) : "—"}
        />
      </div>
      <div>
        <p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Call summary</p>
        <p className="mt-2 rounded-2xl bg-slate-50 px-4 py-3 text-sm text-slate-700">{item.summary}</p>
      </div>
      {item.actionTaken && !item.actionTaken.toLowerCase().startsWith("sarvam voicebot") && (
        <p className="text-sm text-slate-500">{item.actionTaken}</p>
      )}
      {item.transcript && (
        <div>
          <p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Transcript</p>
          <p className="mt-2 max-h-48 overflow-y-auto rounded-2xl border border-slate-100 px-4 py-3 text-sm text-slate-600">{item.transcript}</p>
        </div>
      )}
      {item.needsPersonalContact && item.callbackStatus !== "Completed" && (
        <button className="btn-primary" disabled={busy} onClick={onDone}>
          Mark callback done
        </button>
      )}
    </div>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl bg-slate-50 px-4 py-3">
      <p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">{label}</p>
      <p className="mt-1 font-medium text-slate-800">{value}</p>
    </div>
  );
}
