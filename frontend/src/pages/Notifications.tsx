import { useEffect, useState } from "react";
import { CalendarCheck, PhoneCall } from "lucide-react";
import { api, type Appointment, type AppointmentPage, type DashboardStats } from "../api/client";
import { formatStamp, isoDate } from "../lib/format";

interface Notice {
  id: string;
  title: string;
  detail: string;
  tag: string;
  at: string;
  unread: boolean;
}

export function NotificationsPage() {
  const [notices, setNotices] = useState<Notice[]>([]);
  const [filter, setFilter] = useState<"all" | "unread" | "appointments">("all");

  async function load() {
    const today = isoDate(new Date());
    const from = isoDate(new Date(Date.now() - 29 * 24 * 60 * 60 * 1000));
    const [dash, appts] = await Promise.all([
      api.get<DashboardStats>("/dashboard", { params: { from, to: today, page: 1, pageSize: 30 } }),
      api.get<AppointmentPage>("/appointment", { params: { page: 1, pageSize: 50 } }),
    ]);
    const callNotes: Notice[] = dash.data.actionItems.map((item) => ({
      id: `call-${item.id}`,
      title: item.bookedDoctorName ? "New Appointment Booked" : "Inbound call",
      detail: `${item.callerName}${item.callerPhone ? ` · ${item.callerPhone}` : ""}${item.bookedDoctorName ? ` booked with ${item.bookedDoctorName}` : ""}. ${item.summary}`,
      tag: item.bookedDoctorName ? "Appointment" : "Call",
      at: item.timestamp,
      unread: Boolean(item.needsPersonalContact && item.callbackStatus !== "Completed"),
    }));
    const apptNotes: Notice[] = appts.data.items.map((item: Appointment) => ({
      id: `appt-${item.id}`,
      title: item.status === "Cancelled" ? "Appointment Cancelled" : "Appointment update",
      detail: `${item.patientName} · ${item.patientContact || "No number"} · booked with ${item.doctorName} on ${formatStamp(item.scheduledAt)}`,
      tag: item.status,
      at: item.createdAt || item.scheduledAt,
      unread: item.status === "Pending",
    }));
    setNotices([...callNotes, ...apptNotes].sort((a, b) => +new Date(b.at) - +new Date(a.at)));
  }

  useEffect(() => {
    void load();
  }, []);

  const unreadCount = notices.filter((item) => item.unread).length;
  const visible = notices.filter((item) => {
    if (filter === "unread") return item.unread;
    if (filter === "appointments") return item.tag.toLowerCase().includes("appointment") || item.title.toLowerCase().includes("appointment");
    return true;
  });

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-slate-500">{notices.length} total · {unreadCount} unread</p>
        <div className="flex gap-2">
          {(["all", "unread", "appointments"] as const).map((value) => (
            <button
              key={value}
              className={`rounded-full px-3 py-1.5 text-sm font-semibold capitalize ${filter === value ? "bg-teal-700 text-white" : "bg-white text-slate-600"}`}
              onClick={() => setFilter(value)}
            >
              {value === "all" ? `All (${notices.length})` : value === "unread" ? `Unread (${unreadCount})` : `Appointments (${notices.filter((n) => n.title.toLowerCase().includes("appointment") || n.tag.toLowerCase().includes("appointment")).length})`}
            </button>
          ))}
          <button className="btn-ghost" onClick={() => void load()}>Refresh</button>
        </div>
      </div>
      <section className="card divide-y divide-slate-100">
        {visible.length === 0 ? (
          <p className="px-5 py-8 text-sm text-slate-500">No notifications yet.</p>
        ) : (
          visible.map((item) => (
            <article key={item.id} className="flex gap-4 px-5 py-4">
              <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-teal-50 text-teal-800">
                {item.tag.toLowerCase().includes("call") ? <PhoneCall className="h-4 w-4" /> : <CalendarCheck className="h-4 w-4" />}
              </div>
              <div className="min-w-0 flex-1">
                <div className="flex flex-wrap items-center gap-2">
                  <h2 className="font-semibold text-slate-800">{item.title}</h2>
                  <span className="rounded-full bg-teal-50 px-2 py-0.5 text-[11px] font-semibold text-teal-800">{item.tag}</span>
                  {item.unread && <span className="h-2 w-2 rounded-full bg-sky-500" />}
                </div>
                <p className="mt-1 text-sm text-slate-600">{item.detail}</p>
              </div>
              <p className="whitespace-nowrap text-xs text-slate-400">{formatStamp(item.at)}</p>
            </article>
          ))
        )}
      </section>
    </div>
  );
}
