import { useEffect, useMemo, useRef, useState } from "react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import {
  ArcElement,
  CategoryScale,
  Chart as ChartJS,
  Filler,
  Legend,
  LinearScale,
  LineElement,
  PointElement,
  BarElement,
  Tooltip,
} from "chart.js";
import { Bar, Doughnut, Line } from "react-chartjs-2";
import { CalendarCheck, CircleCheck, Clock3, Phone, PhoneForwarded, PhoneIncoming } from "lucide-react";
import { api, TOKEN_KEY, type CallLog, type DashboardStats } from "../api/client";

ChartJS.register(CategoryScale, LinearScale, PointElement, LineElement, BarElement, ArcElement, Tooltip, Legend, Filler);

function formatStamp(value: string) {
  return new Date(value).toLocaleString([], { dateStyle: "medium", timeStyle: "short" });
}

function isoDate(value: Date) {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
}

function startOfMonth() {
  const now = new Date();
  return isoDate(new Date(now.getFullYear(), now.getMonth(), 1));
}

const ACTION_PAGE_SIZE = 8;

export function DashboardPage() {
  const [stats, setStats] = useState<DashboardStats | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const [fromDate, setFromDate] = useState(isoDate(new Date(Date.now() - 6 * 24 * 60 * 60 * 1000)));
  const [toDate, setToDate] = useState(isoDate(new Date()));
  const [actionPage, setActionPage] = useState(1);
  const rangeRef = useRef({ from: fromDate, to: toDate, page: 1 });
  rangeRef.current = { from: fromDate, to: toDate, page: actionPage };

  async function load(nextFrom = rangeRef.current.from, nextTo = rangeRef.current.to, page = rangeRef.current.page) {
    const { data } = await api.get<DashboardStats>("/dashboard", {
      params: { from: nextFrom, to: nextTo, page, pageSize: ACTION_PAGE_SIZE },
    });
    setStats(data);
    setActionPage(data.actionItemPage);
  }

  function applyPreset(preset: "today" | "week" | "month") {
    const today = isoDate(new Date());
    setActionPage(1);
    if (preset === "today") {
      setFromDate(today);
      setToDate(today);
      void load(today, today, 1);
      return;
    }
    if (preset === "month") {
      const start = startOfMonth();
      setFromDate(start);
      setToDate(today);
      void load(start, today, 1);
      return;
    }
    const weekStart = isoDate(new Date(Date.now() - 6 * 24 * 60 * 60 * 1000));
    setFromDate(weekStart);
    setToDate(today);
    void load(weekStart, today, 1);
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
      setActionPage(1);
      void load(rangeRef.current.from, rangeRef.current.to, 1);
      window.setTimeout(() => setToast(null), 4000);
    });
    connection.on("TransferRequested", (item: { callerName?: string; reason?: string }) => {
      setToast(`Warm transfer — ${item.callerName ?? "Caller"}: ${item.reason ?? "needs a person"}`);
      setActionPage(1);
      void load(rangeRef.current.from, rangeRef.current.to, 1);
      window.setTimeout(() => setToast(null), 6000);
    });
    connection.on("CallbackQueued", (item: { callerName?: string; summary?: string }) => {
      setToast(`Callback queued — ${item.callerName ?? "Caller"}: ${item.summary ?? "call back"}`);
      setActionPage(1);
      void load(rangeRef.current.from, rangeRef.current.to, 1);
      window.setTimeout(() => setToast(null), 5000);
    });

    void connection.start();
    return () => {
      void connection.stop();
    };
  }, []);

  const callChart = useMemo(
    () => ({
      labels: stats?.callVolume.map((x) => x.label) ?? [],
      datasets: [
        {
          label: "Daily call volume",
          data: stats?.callVolume.map((x) => x.count) ?? [],
          borderColor: "#0f766e",
          backgroundColor: "rgba(13,148,136,0.15)",
          fill: true,
          tension: 0.35,
        },
      ],
    }),
    [stats],
  );

  const statusBarChart = useMemo(
    () => ({
      labels: stats?.appointmentStatusByDay.map((x) => x.label) ?? [],
      datasets: [
        {
          label: "Pending",
          data: stats?.appointmentStatusByDay.map((x) => x.pending) ?? [],
          backgroundColor: "#f59e0b",
          borderRadius: 6,
          stack: "status",
        },
        {
          label: "Completed",
          data: stats?.appointmentStatusByDay.map((x) => x.completed) ?? [],
          backgroundColor: "#0d9488",
          borderRadius: 6,
          stack: "status",
        },
        {
          label: "Cancelled",
          data: stats?.appointmentStatusByDay.map((x) => x.cancelled) ?? [],
          backgroundColor: "#e11d48",
          borderRadius: 6,
          stack: "status",
        },
      ],
    }),
    [stats],
  );

  const statusPieChart = useMemo(
    () => ({
      labels: ["Pending", "Completed", "Cancelled"],
      datasets: [
        {
          data: [stats?.pendingAppointments ?? 0, stats?.completedAppointments ?? 0, stats?.cancelledAppointments ?? 0],
          backgroundColor: ["#f59e0b", "#0d9488", "#e11d48"],
          borderWidth: 0,
        },
      ],
    }),
    [stats],
  );

  if (!stats) {
    return <p className="text-slate-500">Loading dashboard…</p>;
  }

  const cards = [
    { label: "Calls in range", value: stats.callsToday, icon: Phone },
    { label: "AI containment", value: `${stats.containmentRate ?? 0}%`, icon: CircleCheck },
    { label: "Escalated", value: stats.escalatedCalls ?? 0, icon: PhoneForwarded },
    { label: "Callback queue", value: stats.callbackQueued ?? 0, icon: PhoneIncoming },
    { label: "Booked", value: stats.bookedAppointments, icon: CalendarCheck },
    { label: "Pending", value: stats.pendingAppointments, icon: Clock3 },
    { label: "Completed", value: stats.completedAppointments, icon: CircleCheck },
  ];

  return (
    <div className="space-y-6">
      {toast && (
        <div className="rounded-2xl border border-teal-200 bg-teal-50 px-4 py-3 text-sm text-teal-900">
          Live update — {toast}
        </div>
      )}
      <section className="card flex flex-wrap items-end gap-3 p-4">
        <div>
          <label>From</label>
          <input className="w-auto" type="date" value={fromDate} max={toDate} onChange={(e) => setFromDate(e.target.value)} />
        </div>
        <div>
          <label>To</label>
          <input className="w-auto" type="date" value={toDate} min={fromDate} onChange={(e) => setToDate(e.target.value)} />
        </div>
        <button
          className="btn-primary"
          onClick={() => {
            setActionPage(1);
            void load(fromDate, toDate, 1);
          }}
        >
          Apply
        </button>
        <button className="btn-ghost" onClick={() => applyPreset("today")}>
          Today
        </button>
        <button className="btn-ghost" onClick={() => applyPreset("week")}>
          Last 7 days
        </button>
        <button className="btn-ghost" onClick={() => applyPreset("month")}>
          This month
        </button>
        <p className="ml-auto text-sm text-slate-500">
          Showing {new Date(fromDate).toLocaleDateString()} – {new Date(toDate).toLocaleDateString()}
        </p>
      </section>
      <section className="grid gap-4 sm:grid-cols-2 xl:grid-cols-4">
        {cards.map((card) => (
          <div key={card.label} className="card p-5">
            <div className="flex items-center justify-between">
              <p className="text-sm text-slate-500">{card.label}</p>
              <card.icon className="h-4 w-4 text-teal-700" />
            </div>
            <p className="mt-3 font-display text-4xl">{card.value}</p>
          </div>
        ))}
      </section>

      <section className="grid gap-4 xl:grid-cols-2">
        <div className="card p-5">
          <h2 className="mb-1 font-display text-xl">Appointments by status</h2>
          <p className="mb-4 text-sm text-slate-500">Pending vs completed vs cancelled for the selected dates.</p>
          <Bar
            data={statusBarChart}
            options={{
              plugins: { legend: { position: "bottom" } },
              scales: { x: { stacked: true }, y: { stacked: true, beginAtZero: true, ticks: { precision: 0 } } },
            }}
          />
        </div>
        <div className="card p-5">
          <h2 className="mb-1 font-display text-xl">Status split</h2>
          <p className="mb-4 text-sm text-slate-500">
            Booked {stats.bookedAppointments} · Pending {stats.pendingAppointments} · Completed {stats.completedAppointments}
          </p>
          <div className="mx-auto max-w-xs">
            <Doughnut data={statusPieChart} options={{ plugins: { legend: { position: "bottom" } }, cutout: "62%" }} />
          </div>
        </div>
      </section>

      <section className="card p-5">
        <h2 className="mb-4 font-display text-xl">Daily call volume</h2>
        <Line data={callChart} options={{ plugins: { legend: { display: false } }, scales: { y: { beginAtZero: true, ticks: { precision: 0 } } } }} />
      </section>

      <section className="card overflow-hidden">
        <div className="border-b border-slate-100 px-5 py-4">
          <h2 className="font-display text-xl">Action items</h2>
          <p className="text-sm text-slate-500">Each inbound call is summarized by the AI agent and pushed here over SignalR.</p>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-500">
              <tr>
                <th className="px-5 py-3 font-medium">Caller</th>
                <th className="px-5 py-3 font-medium">Summary</th>
                <th className="px-5 py-3 font-medium">Action taken</th>
                <th className="px-5 py-3 font-medium">Outcome</th>
                <th className="px-5 py-3 font-medium">Timestamp</th>
              </tr>
            </thead>
            <tbody>
              {stats.actionItems.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-5 py-8 text-center text-slate-500">
                    No action items in this date range.
                  </td>
                </tr>
              ) : (
                stats.actionItems.map((item) => (
                  <tr key={item.id} className="border-t border-slate-100">
                    <td className="px-5 py-3 font-medium text-slate-800">{item.callerName}</td>
                    <td className="px-5 py-3 text-slate-600">{item.summary}</td>
                    <td className="px-5 py-3">
                      <span className="rounded-full bg-teal-50 px-2 py-1 text-xs font-semibold text-teal-800">{item.actionTaken}</span>
                    </td>
                    <td className="px-5 py-3">
                      <span className="rounded-full bg-slate-100 px-2 py-1 text-xs font-semibold text-slate-700">
                        {item.outcome ?? "Contained"}
                        {item.escalationReason ? ` · ${item.escalationReason}` : ""}
                      </span>
                    </td>
                    <td className="px-5 py-3 text-slate-500">{formatStamp(item.timestamp)}</td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        {stats.actionItemTotal > 0 && (
          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 px-5 py-3">
            <p className="text-sm text-slate-500">
              Showing {(stats.actionItemPage - 1) * stats.actionItemPageSize + 1}–
              {Math.min(stats.actionItemPage * stats.actionItemPageSize, stats.actionItemTotal)} of {stats.actionItemTotal}
            </p>
            <div className="flex items-center gap-2">
              <button
                className="btn-ghost"
                disabled={stats.actionItemPage <= 1}
                onClick={() => {
                  const next = stats.actionItemPage - 1;
                  setActionPage(next);
                  void load(fromDate, toDate, next);
                }}
              >
                Previous
              </button>
              <span className="text-sm text-slate-600">
                Page {stats.actionItemPage} of {Math.max(1, Math.ceil(stats.actionItemTotal / stats.actionItemPageSize))}
              </span>
              <button
                className="btn-ghost"
                disabled={stats.actionItemPage * stats.actionItemPageSize >= stats.actionItemTotal}
                onClick={() => {
                  const next = stats.actionItemPage + 1;
                  setActionPage(next);
                  void load(fromDate, toDate, next);
                }}
              >
                Next
              </button>
            </div>
          </div>
        )}
      </section>
    </div>
  );
}
