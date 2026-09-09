import { useEffect, useMemo, useState } from "react";
import { api, type CallLog, type DashboardStats } from "../api/client";
import { StatusBadge } from "../components/StatusBadge";
import { formatStamp, isoDate } from "../lib/format";
import { useAuth } from "../context/AuthContext";

export function SupportPage() {
  const { user } = useAuth();
  if (user?.role === "Patient") {
    return <PatientSupport />;
  }

  return <StaffSupport />;
}

function PatientSupport() {
  const [items, setItems] = useState<CallLog[]>([]);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);

  async function load() {
    const { data } = await api.get<CallLog[]>("/patients/me/tickets");
    setItems(data);
  }

  useEffect(() => {
    void load();
  }, []);

  async function send() {
    setError("");
    setNotice("");
    setBusy(true);
    try {
      await api.post("/patients/me/tickets", { message });
      setMessage("");
      setNotice("Sent. The clinic will contact you.");
      await load();
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setError(axiosErr.response?.data?.message ?? "Could not send.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="font-display text-4xl text-slate-900">Support</h2>
        <p className="mt-1 text-sm text-slate-500">Send a message and the clinic will call or email you back.</p>
      </div>
      {notice && <p className="rounded-xl bg-teal-50 px-3 py-2 text-sm text-teal-800">{notice}</p>}
      {error && <p className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>}
      <section className="card space-y-3 p-5">
        <label>Your message</label>
        <textarea rows={4} value={message} onChange={(e) => setMessage(e.target.value)} placeholder="How can we help?" />
        <button className="btn-primary" disabled={busy} onClick={() => void send()}>
          {busy ? "Sending..." : "Send to clinic"}
        </button>
      </section>
      <section className="card divide-y divide-slate-100">
        {items.length === 0 ? (
          <p className="px-5 py-8 text-sm text-slate-500">No previous requests.</p>
        ) : (
          items.map((item) => (
            <article key={item.id} className="px-5 py-4">
              <div className="flex items-center justify-between gap-2">
                <p className="font-medium text-slate-800">{item.summary}</p>
                <StatusBadge status={item.callbackStatus === "Completed" ? "resolved" : "in progress"} />
              </div>
              <p className="mt-1 text-xs text-slate-400">{formatStamp(item.timestamp)}</p>
            </article>
          ))
        )}
      </section>
    </div>
  );
}

function StaffSupport() {
  const [items, setItems] = useState<CallLog[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [status, setStatus] = useState<"Open" | "In Progress" | "Resolved">("In Progress");
  const [reply, setReply] = useState("");
  const [busy, setBusy] = useState(false);
  const [query, setQuery] = useState("");

  async function load() {
    const today = isoDate(new Date());
    const from = isoDate(new Date(Date.now() - 29 * 24 * 60 * 60 * 1000));
    const { data } = await api.get<DashboardStats>("/dashboard", { params: { from, to: today, page: 1, pageSize: 50 } });
    const tickets = data.actionItems.filter((item) => item.needsPersonalContact || item.callbackStatus);
    setItems(tickets);
    setSelectedId((current) => current ?? tickets[0]?.id ?? null);
  }

  useEffect(() => {
    void load();
  }, []);

  const visible = useMemo(() => {
    const term = query.trim().toLowerCase();
    return items.filter(
      (item) =>
        !term ||
        item.callerName.toLowerCase().includes(term) ||
        item.callerPhone.toLowerCase().includes(term) ||
        item.summary.toLowerCase().includes(term),
    );
  }, [items, query]);

  const selected = visible.find((item) => item.id === selectedId) ?? visible[0] ?? null;
  const ticketStatus = useMemo(() => {
    if (!selected) return "Open";
    if (selected.callbackStatus === "Completed") return "Resolved";
    if (selected.callbackStatus === "Required" || selected.callbackStatus === "Queued") return status === "Resolved" ? "Resolved" : status;
    return "Open";
  }, [selected, status]);

  async function resolve() {
    if (!selected) return;
    setBusy(true);
    try {
      await api.post(`/dashboard/calls/${selected.id}/callback/complete`);
      setReply("");
      await load();
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="grid gap-4 xl:grid-cols-[320px_1fr]">
      <section className="card overflow-hidden">
        <div className="border-b border-slate-100 p-4">
          <input value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search tickets..." />
        </div>
        {visible.length === 0 ? (
          <p className="px-4 py-8 text-sm text-slate-500">No callback tickets right now.</p>
        ) : (
          visible.map((item) => (
            <button
              key={item.id}
              onClick={() => setSelectedId(item.id)}
              className={`w-full border-b border-slate-100 px-4 py-3 text-left ${selected?.id === item.id ? "bg-teal-50" : "hover:bg-slate-50"}`}
            >
              <div className="flex items-center justify-between gap-2">
                <p className="font-medium text-slate-800">{item.callerName}</p>
                <StatusBadge status={item.callbackStatus === "Completed" ? "resolved" : "in progress"} />
              </div>
              <p className="mt-1 text-xs text-slate-500">Patient · {item.callerPhone || "No number"}</p>
              <p className="mt-1 text-xs text-slate-400">{formatStamp(item.timestamp)}</p>
            </button>
          ))
        )}
      </section>
      <section className="card p-5">
        {!selected ? (
          <p className="text-slate-500">Select a ticket.</p>
        ) : (
          <div className="space-y-4">
            <div>
              <h2 className="font-display text-2xl">{selected.callerName}</h2>
              <p className="text-sm text-slate-500">{selected.callerPhone || "No number"} · opened {formatStamp(selected.timestamp)}</p>
            </div>
            <div className="flex flex-wrap gap-2">
              {(["Open", "In Progress", "Resolved"] as const).map((value) => (
                <button
                  key={value}
                  className={`rounded-full px-3 py-1.5 text-sm font-semibold ${
                    ticketStatus === value
                      ? value === "In Progress"
                        ? "bg-amber-500 text-white"
                        : value === "Open"
                          ? "bg-sky-500 text-white"
                          : "bg-teal-700 text-white"
                      : "bg-slate-100 text-slate-600"
                  }`}
                  onClick={() => setStatus(value)}
                >
                  {value}
                </button>
              ))}
            </div>
            <div className="space-y-3 rounded-2xl bg-slate-50 p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Patient</p>
              <p className="rounded-2xl bg-white px-4 py-3 text-sm text-slate-700">{selected.summary}</p>
              {selected.bookedDoctorName && (
                <p className="text-sm text-slate-600">Booked with {selected.bookedDoctorName}.</p>
              )}
            </div>
            <textarea value={reply} onChange={(e) => setReply(e.target.value)} placeholder="Type your reply... (Enter to send)" rows={3} />
            <button className="btn-primary" disabled={busy} onClick={() => void resolve()}>
              Mark resolved
            </button>
          </div>
        )}
      </section>
    </div>
  );
}
