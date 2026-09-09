import { useEffect, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { PhoneIncoming } from "lucide-react";
import { api, TOKEN_KEY, type CallLog, type DashboardStats } from "../api/client";

function formatStamp(value: string) {
  return new Date(value).toLocaleString([], { dateStyle: "medium", timeStyle: "short" });
}

export function VoiceAgentPage() {
  const [calls, setCalls] = useState<CallLog[]>([]);
  const [toast, setToast] = useState<string | null>(null);

  async function load() {
    const today = new Date().toISOString().slice(0, 10);
    const { data } = await api.get<DashboardStats>("/dashboard", {
      params: { from: today, to: today, page: 1, pageSize: 20 },
    });
    setCalls(data.actionItems);
  }

  useEffect(() => {
    void load();
    const token = localStorage.getItem(TOKEN_KEY);
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/dashboard", { accessTokenFactory: () => token ?? "" })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();

    connection.on("CallSummaryAdded", (item: CallLog) => {
      setToast(`${item.callerName}: ${item.summary}`);
      void load();
      window.setTimeout(() => setToast(null), 4000);
    });
    connection.on("TransferRequested", (item: { callerName?: string; reason?: string }) => {
      setToast(`Transfer — ${item.callerName ?? "Caller"}: ${item.reason ?? "needs a person"}`);
      void load();
      window.setTimeout(() => setToast(null), 6000);
    });
    connection.on("CallbackQueued", (item: { callerName?: string; summary?: string }) => {
      setToast(`Callback — ${item.callerName ?? "Caller"}: ${item.summary ?? "call back"}`);
      void load();
      window.setTimeout(() => setToast(null), 6000);
    });

    void connection.start();
    return () => {
      void connection.stop();
    };
  }, []);

  return (
    <div className="space-y-6">
      {toast && <div className="rounded-2xl bg-teal-50 px-4 py-3 text-sm text-teal-900">{toast}</div>}
      <section className="card space-y-3 p-5">
        <div className="flex items-center gap-3">
          <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-teal-100 text-teal-800">
            <PhoneIncoming className="h-5 w-5" />
          </div>
          <div>
            <h2 className="font-display text-2xl">Inbound clinic calls</h2>
            <p className="text-sm text-slate-500">
              Live calls go Exotel Voicebot → Sarvam. When the call ends, Sarvam posts the transcript here. Booked
              slots also appear on Appointments.
            </p>
          </div>
        </div>
      </section>

      <section className="card p-5">
        <h3 className="font-display text-xl">Today</h3>
        {calls.length === 0 ? (
          <p className="mt-3 text-sm text-slate-500">No inbound calls yet today.</p>
        ) : (
          <ul className="mt-4 space-y-3">
            {calls.map((call) => (
              <li key={call.id} className="rounded-2xl border border-slate-100 p-4">
                <div className="flex flex-wrap items-center justify-between gap-2">
                  <p className="font-medium text-slate-900">
                    {call.callerName}{" "}
                    <span className="text-sm font-normal text-slate-500">{call.callerPhone || "No number"}</span>
                  </p>
                  <p className="text-xs text-slate-500">{formatStamp(call.timestamp)}</p>
                </div>
                {call.bookedDoctorName && (
                  <p className="mt-2 text-sm font-medium text-teal-800">
                    Booked with {call.bookedDoctorName}
                    {call.appointmentTime ? ` · ${formatStamp(call.appointmentTime)}` : ""}
                  </p>
                )}
                <p className="mt-2 text-sm text-slate-700">{call.summary}</p>
                <div className="mt-2 flex flex-wrap gap-2 text-xs">
                  <span className="rounded-full bg-teal-50 px-2 py-1 font-semibold text-teal-800">{call.intent}</span>
                  {call.outcome && <span className="rounded-full bg-slate-100 px-2 py-1 text-slate-700">{call.outcome}</span>}
                  <span className="text-slate-500">{call.actionTaken}</span>
                </div>
                {call.transcript && <p className="mt-2 text-sm text-slate-600">{call.transcript}</p>}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  );
}
