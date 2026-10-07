import { useEffect, useMemo, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { api, TOKEN_KEY, type CallLog } from "../api/client";
import { StatusBadge } from "../components/StatusBadge";
import { formatStamp } from "../lib/format";
import { useAuth } from "../context/AuthContext";
import { useSuccessPopup } from "../components/SuccessPopup";
import { Pagination } from "../components/Pagination";
import { pagerProps, usePaged } from "../lib/pager";

export function SupportPage() {
  const { user } = useAuth();
  if (user?.role === "Patient") {
    return <PatientSupport />;
  }

  return <StaffSupport />;
}

function PatientSupport() {
  const { showSuccess } = useSuccessPopup();
  const [items, setItems] = useState<CallLog[]>([]);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [notice, setNotice] = useState("");
  const [busy, setBusy] = useState(false);
  const pagedItems = usePaged(items, 8);

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
    if (message.trim().length < 3) {
      setError("Write a short message so the clinic can help.");
      return;
    }
    setBusy(true);
    try {
      await api.post("/patients/me/tickets", { message });
      setMessage("");
      setNotice("Sent. Clinic staff will see this in Support and Notifications.");
      showSuccess("Details Submitted");
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
        <button className="btn-primary" disabled={busy || message.trim().length < 3} onClick={() => void send()}>
          {busy ? "Sending..." : "Send to clinic"}
        </button>
      </section>
      <section className="card divide-y divide-slate-100">
        {items.length === 0 ? (
          <p className="px-5 py-8 text-sm text-slate-500">No previous requests.</p>
        ) : (
          pagedItems.items.map((item) => (
            <article key={item.id} className="px-5 py-4">
              <div className="flex items-center justify-between gap-2">
                <p className="font-medium text-slate-800">{item.summary}</p>
                <StatusBadge status={item.callbackStatus === "Completed" ? "resolved" : "in progress"} />
              </div>
              {item.actionTaken
                && (item.actionTaken.startsWith("Support reply:") || item.actionTaken === "Support ticket resolved") && (
                  <p className="mt-2 rounded-xl bg-teal-50 px-3 py-2 text-sm text-teal-900">
                    Clinic reply: {item.actionTaken.replace(/^Support reply:\s*/i, "")}
                  </p>
                )}
              <p className="mt-1 text-xs text-slate-400">{formatStamp(item.timestamp)}</p>
            </article>
          ))
        )}
        <Pagination {...pagerProps(pagedItems)} />
      </section>
    </div>
  );
}

function StaffSupport() {
  const { showSuccess } = useSuccessPopup();
  const [items, setItems] = useState<CallLog[]>([]);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [reply, setReply] = useState("");
  const [busy, setBusy] = useState(false);
  const [query, setQuery] = useState("");
  const [error, setError] = useState("");

  async function load() {
    const { data } = await api.get<CallLog[]>("/support/tickets");
    setItems(data);
    setSelectedId((current) => current ?? data[0]?.id ?? null);
  }

  useEffect(() => {
    void load();
    const token = localStorage.getItem(TOKEN_KEY);
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/dashboard", { accessTokenFactory: () => token ?? "" })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();
    connection.on("CallbackQueued", () => void load());
    connection.on("NotificationAdded", () => void load());
    void connection.start();
    return () => {
      void connection.stop();
    };
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
  const pagedTickets = usePaged(visible, 8, query);

  const selected = visible.find((item) => item.id === selectedId) ?? visible[0] ?? null;

  async function resolve() {
    if (!selected) return;
    setBusy(true);
    setError("");
    try {
      await api.post(`/support/tickets/${selected.id}/resolve`, { reply });
      setReply("");
      showSuccess("Details Submitted");
      await load();
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setError(axiosErr.response?.data?.message ?? "Could not resolve ticket.");
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
          <p className="px-4 py-8 text-sm text-slate-500">No support tickets yet.</p>
        ) : (
          pagedTickets.items.map((item) => (
            <button
              key={item.id}
              onClick={() => setSelectedId(item.id)}
              className={`w-full border-b border-slate-100 px-4 py-3 text-left ${selected?.id === item.id ? "bg-teal-50" : "hover:bg-slate-50"}`}
            >
              <div className="flex items-center justify-between gap-2">
                <p className="font-medium text-slate-800">{item.callerName}</p>
                <StatusBadge status={item.callbackStatus === "Completed" ? "resolved" : "open"} />
              </div>
              <p className="mt-1 line-clamp-2 text-sm text-slate-600">{item.summary}</p>
              <p className="mt-1 text-xs text-slate-400">{formatStamp(item.timestamp)}</p>
            </button>
          ))
        )}
        <Pagination {...pagerProps(pagedTickets)} />
      </section>
      <section className="card p-5">
        {!selected ? (
          <p className="text-slate-500">Select a ticket.</p>
        ) : (
          <div className="space-y-4">
            <div>
              <h2 className="font-display text-2xl">{selected.callerName}</h2>
              <p className="text-sm text-slate-500">
                {selected.callerPhone || "No number"} · opened {formatStamp(selected.timestamp)}
              </p>
            </div>
            {error && <p className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>}
            <div className="space-y-3 rounded-2xl bg-slate-50 p-4">
              <p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Patient message</p>
              <p className="rounded-2xl bg-white px-4 py-3 text-sm text-slate-700">{selected.summary}</p>
            </div>
            <textarea
              value={reply}
              onChange={(e) => setReply(e.target.value)}
              placeholder="Optional reply note..."
              rows={3}
              disabled={selected.callbackStatus === "Completed"}
            />
            <button className="btn-primary" disabled={busy || selected.callbackStatus === "Completed"} onClick={() => void resolve()}>
              {selected.callbackStatus === "Completed" ? "Resolved" : busy ? "Saving..." : "Mark resolved"}
            </button>
          </div>
        )}
      </section>
    </div>
  );
}
