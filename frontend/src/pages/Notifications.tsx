import { useEffect, useState } from "react";
import { CalendarCheck, LifeBuoy, PhoneCall } from "lucide-react";
import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { api, TOKEN_KEY, type ClinicNotification } from "../api/client";
import { formatStamp } from "../lib/format";
import { Pagination } from "../components/Pagination";
import { pagerProps, usePaged } from "../lib/pager";

export function NotificationsPage() {
  const [notices, setNotices] = useState<ClinicNotification[]>([]);
  const [filter, setFilter] = useState<"all" | "unread" | "appointments" | "support">("all");

  async function load() {
    const { data } = await api.get<ClinicNotification[]>("/notifications");
    setNotices(data);
  }

  useEffect(() => {
    void load();
    const token = localStorage.getItem(TOKEN_KEY);
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/dashboard", { accessTokenFactory: () => token ?? "" })
      .withAutomaticReconnect()
      .configureLogging(LogLevel.None)
      .build();
    connection.on("NotificationAdded", () => void load());
    connection.on("AppointmentChanged", () => void load());
    connection.on("CallbackQueued", () => void load());
    void connection.start();
    return () => {
      void connection.stop();
    };
  }, []);

  async function markAllRead() {
    await api.post("/notifications/read-all");
    await load();
  }

  async function markRead(id: number) {
    await api.post(`/notifications/${id}/read`);
    setNotices((current) => current.map((item) => (item.id === id ? { ...item, isRead: true } : item)));
  }

  const unreadCount = notices.filter((item) => !item.isRead).length;
  const visible = notices.filter((item) => {
    if (filter === "unread") return !item.isRead;
    if (filter === "appointments") return item.tag.toLowerCase().includes("appointment");
    if (filter === "support") return item.tag.toLowerCase().includes("support");
    return true;
  });
  const pagedNotices = usePaged(visible, 8, filter);

  function iconFor(tag: string) {
    const value = tag.toLowerCase();
    if (value.includes("support")) return LifeBuoy;
    if (value.includes("call")) return PhoneCall;
    return CalendarCheck;
  }

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="text-sm text-slate-500">
          {notices.length} total · {unreadCount} unread
        </p>
        <div className="flex flex-wrap gap-2">
          {(["all", "unread", "appointments", "support"] as const).map((value) => (
            <button
              key={value}
              className={`rounded-full px-3 py-1.5 text-sm font-semibold capitalize ${filter === value ? "bg-teal-700 text-white" : "bg-white text-slate-600"}`}
              onClick={() => setFilter(value)}
            >
              {value}
            </button>
          ))}
          <button className="btn-ghost" onClick={() => void markAllRead()} disabled={unreadCount === 0}>
            Mark all read
          </button>
          <button className="btn-ghost" onClick={() => void load()}>
            Refresh
          </button>
        </div>
      </div>
      <section className="card divide-y divide-slate-100">
        {visible.length === 0 ? (
          <p className="px-5 py-8 text-sm text-slate-500">No notifications yet.</p>
        ) : (
          pagedNotices.items.map((item) => {
            const Icon = iconFor(item.tag);
            return (
              <article
                key={item.id}
                className={`flex cursor-pointer gap-4 px-5 py-4 ${item.isRead ? "" : "bg-teal-50/40"}`}
                onClick={() => {
                  if (!item.isRead) void markRead(item.id);
                }}
              >
                <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-teal-50 text-teal-800">
                  <Icon className="h-4 w-4" />
                </div>
                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-2">
                    <h2 className="font-semibold text-slate-800">{item.title}</h2>
                    <span className="rounded-full bg-teal-50 px-2 py-0.5 text-[11px] font-semibold text-teal-800">{item.tag}</span>
                    {!item.isRead && <span className="h-2 w-2 rounded-full bg-sky-500" />}
                  </div>
                  <p className="mt-1 text-sm text-slate-600">{item.detail}</p>
                </div>
                <p className="whitespace-nowrap text-xs text-slate-400">{formatStamp(item.createdAt)}</p>
              </article>
            );
          })
        )}
        <Pagination {...pagerProps(pagedNotices)} />
      </section>
    </div>
  );
}
