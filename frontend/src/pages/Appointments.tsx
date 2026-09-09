import { useEffect, useMemo, useState, type FormEvent } from "react";
import { ChevronDown, ChevronLeft, ChevronRight, Clock3, Eye, Mail, Phone, Plus, X } from "lucide-react";
import { api, type Appointment, type AppointmentPage, type Doctor, type Patient } from "../api/client";
import { StatusBadge } from "../components/StatusBadge";
import { formatDay, formatLongDay, formatMonthDay, formatTime, isoDate, sameDay, toInputValue } from "../lib/format";
import { useAuth } from "../context/AuthContext";

const STATUSES = ["Pending", "Scheduled", "Completed", "Cancelled", "No Show"];

export function AppointmentsPage() {
  const { user } = useAuth();
  const canManage = user?.role === "Admin" || user?.role === "Doctor";
  const isPatient = user?.role === "Patient";
  const [appointments, setAppointments] = useState<Appointment[]>([]);
  const [patients, setPatients] = useState<Patient[]>([]);
  const [doctors, setDoctors] = useState<Doctor[]>([]);
  const [selectedDay, setSelectedDay] = useState(new Date());
  const [month, setMonth] = useState(new Date());
  const [query, setQuery] = useState("");
  const [statusFilter, setStatusFilter] = useState("all");
  const [openBook, setOpenBook] = useState(false);
  const [detail, setDetail] = useState<Appointment | null>(null);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [form, setForm] = useState({
    patientId: 0,
    doctorId: 0,
    scheduledAt: toInputValue(new Date(Date.now() + 60 * 60 * 1000)),
    notes: "",
  });

  async function load() {
    const [pat, doc, appt] = await Promise.all([
      api.get<Patient[]>("/patients"),
      api.get<Doctor[]>("/doctors"),
      api.get<AppointmentPage>("/appointment", { params: { page: 1, pageSize: 100 } }),
    ]);
    setPatients(pat.data);
    const active = doc.data.filter((d) => d.isActive);
    setDoctors(active);
    setAppointments(appt.data.items);
    setForm((current) => ({
      ...current,
      patientId: current.patientId || pat.data[0]?.id || 0,
      doctorId: current.doctorId || active[0]?.id || 0,
    }));
  }

  useEffect(() => {
    void load();
  }, []);

  const dayItems = useMemo(
    () => appointments.filter((item) => sameDay(item.scheduledAt, selectedDay)).sort((a, b) => +new Date(a.scheduledAt) - +new Date(b.scheduledAt)),
    [appointments, selectedDay],
  );
  const upcoming = useMemo(
    () => appointments.filter((item) => new Date(item.scheduledAt) >= new Date() && item.status !== "Cancelled").slice(0, 8),
    [appointments],
  );
  const todayIso = isoDate(new Date());
  const todayItems = appointments.filter((item) => isoDate(new Date(item.scheduledAt)) === todayIso && item.status !== "Cancelled");
  const filtered = appointments.filter((item) => {
    const term = query.trim().toLowerCase();
    const matchesTerm =
      !term ||
      item.patientName.toLowerCase().includes(term) ||
      item.patientContact.toLowerCase().includes(term) ||
      item.doctorName.toLowerCase().includes(term);
    const matchesStatus = statusFilter === "all" || item.status === statusFilter;
    return matchesTerm && matchesStatus;
  });

  async function book(e: FormEvent) {
    e.preventDefault();
    setError("");
    setMessage("");
    try {
      await api.post("/appointment/book", {
        patientId: form.patientId,
        doctorId: form.doctorId,
        scheduledAt: new Date(form.scheduledAt).toISOString(),
        notes: form.notes,
      });
      setOpenBook(false);
      setMessage("Appointment booked.");
      await load();
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setError(axiosErr.response?.data?.message ?? "Booking failed.");
    }
  }

  async function setStatus(id: number, status: string) {
    await api.patch(`/appointment/${id}/status`, { status });
    await load();
  }

  async function reschedule(id: number, scheduledAt: string) {
    setError("");
    setMessage("");
    await api.patch(`/appointment/${id}/reschedule`, { scheduledAt: new Date(scheduledAt).toISOString() });
    setMessage("Appointment rescheduled.");
    await load();
  }

  const monthStart = new Date(month.getFullYear(), month.getMonth(), 1);
  const gridStart = new Date(monthStart);
  gridStart.setDate(monthStart.getDate() - monthStart.getDay());
  const days = Array.from({ length: 42 }, (_, i) => {
    const date = new Date(gridStart);
    date.setDate(gridStart.getDate() + i);
    return date;
  });

  if (isPatient) {
    return (
      <PatientAppointments
        appointments={appointments}
        patient={patients[0]}
        email={user?.email ?? patients[0]?.email ?? ""}
        message={message}
        error={error}
        onRefresh={() => {
          setError("");
          setMessage("");
          void load();
        }}
        onCancel={async (id) => {
          setError("");
          try {
            await setStatus(id, "Cancelled");
            setMessage("Appointment cancelled.");
          } catch (err: unknown) {
            const axiosErr = err as { response?: { data?: { message?: string } } };
            setError(axiosErr.response?.data?.message ?? "Could not cancel.");
          }
        }}
        onReschedule={async (id, scheduledAt) => {
          try {
            await reschedule(id, scheduledAt);
          } catch (err: unknown) {
            const axiosErr = err as { response?: { data?: { message?: string } } };
            setError(axiosErr.response?.data?.message ?? "Could not reschedule.");
            throw err;
          }
        }}
      />
    );
  }

  return (
    <div className="space-y-5">
      {message && <p className="rounded-xl bg-teal-50 px-3 py-2 text-sm text-teal-800">{message}</p>}
      <section className="grid gap-3 sm:grid-cols-2 xl:grid-cols-4">
        <Metric label="Today's total" value={todayItems.length} className="bg-teal-50" />
        <Metric label="Confirmed" value={todayItems.filter((i) => i.status === "Scheduled" || i.status === "Confirmed").length} className="bg-emerald-50" />
        <Metric label="Pending" value={todayItems.filter((i) => i.status === "Pending").length} className="bg-amber-50" />
        <Metric label="Completed" value={appointments.filter((i) => i.status === "Completed" && isoDate(new Date(i.scheduledAt)) === todayIso).length} className="bg-rose-50" />
      </section>

      <section className="grid gap-4 xl:grid-cols-[320px_1fr]">
        <div className="card p-4">
          <div className="mb-3 flex items-center justify-between">
            <h2 className="font-display text-lg">{month.toLocaleString([], { month: "long", year: "numeric" })}</h2>
            <div className="flex gap-1">
              <button className="btn-ghost px-2" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() - 1, 1))}><ChevronLeft className="h-4 w-4" /></button>
              <button className="btn-ghost px-2" onClick={() => setMonth(new Date(month.getFullYear(), month.getMonth() + 1, 1))}><ChevronRight className="h-4 w-4" /></button>
            </div>
          </div>
          <div className="grid grid-cols-7 gap-1 text-center text-[11px] font-semibold uppercase text-slate-400">
            {"SMTWTFS".split("").map((d, i) => <span key={i}>{d}</span>)}
          </div>
          <div className="mt-2 grid grid-cols-7 gap-1">
            {days.map((date) => {
              const has = appointments.some((item) => sameDay(item.scheduledAt, date));
              const selected = sameDay(date, selectedDay);
              const inMonth = date.getMonth() === month.getMonth();
              return (
                <button
                  key={date.toISOString()}
                  onClick={() => setSelectedDay(date)}
                  className={`rounded-xl py-2 text-sm ${selected ? "bg-teal-700 text-white" : inMonth ? "text-slate-700 hover:bg-slate-50" : "text-slate-300"}`}
                >
                  {date.getDate()}
                  {has && <span className={`mx-auto mt-1 block h-1.5 w-1.5 rounded-full ${selected ? "bg-white" : "bg-teal-500"}`} />}
                </button>
              );
            })}
          </div>
        </div>
        <div className="card p-4">
          <h2 className="font-display text-lg">{formatDay(selectedDay)}</h2>
          <p className="text-sm text-slate-500">{dayItems.length} appointments</p>
          <div className="mt-4 space-y-3">
            {dayItems.length === 0 ? (
              <p className="text-sm text-slate-500">No visits on this day.</p>
            ) : (
              dayItems.map((item) => (
                <button key={item.id} onClick={() => setDetail(item)} className="flex w-full items-start justify-between rounded-2xl border border-slate-100 px-4 py-3 text-left hover:bg-slate-50">
                  <div>
                    <p className="text-sm font-semibold text-teal-800">{formatTime(item.scheduledAt)}</p>
                    <p className="font-medium text-slate-800">{item.patientName}</p>
                    <p className="text-xs text-slate-500">{item.notes || "Clinic visit"} · {item.patientContact || "No number"}</p>
                    <p className="text-xs text-slate-500">Booked with {item.doctorName}</p>
                  </div>
                  <StatusBadge status={item.status} />
                </button>
              ))
            )}
          </div>
        </div>
      </section>

      <section className="card overflow-hidden">
        <div className="flex flex-wrap items-center justify-between gap-3 border-b border-slate-100 px-5 py-4">
          <div>
            <h2 className="font-display text-xl">Patient Appointments</h2>
            <p className="text-sm text-slate-500">{appointments.length} total · updates in real time</p>
          </div>
          {canManage && (
            <button className="btn-primary" onClick={() => setOpenBook(true)}>
              <Plus className="h-4 w-4" /> New Appointment
            </button>
          )}
        </div>
        <div className="flex flex-wrap gap-3 px-5 py-3">
          <input className="max-w-sm" value={query} onChange={(e) => setQuery(e.target.value)} placeholder="Search by name, phone or doctor..." />
          <select className="w-auto" value={statusFilter} onChange={(e) => setStatusFilter(e.target.value)}>
            <option value="all">All Statuses</option>
            {STATUSES.map((status) => (
              <option key={status} value={status}>
                {status === "Scheduled" ? "Confirmed" : status}
              </option>
            ))}
          </select>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-500">
              <tr>
                <th className="px-5 py-3 font-medium">Patient</th>
                <th className="px-5 py-3 font-medium">Contact number</th>
                <th className="px-5 py-3 font-medium">Booked with</th>
                <th className="px-5 py-3 font-medium">Call summary</th>
                <th className="px-5 py-3 font-medium">Scheduled</th>
                <th className="px-5 py-3 font-medium">Status</th>
                <th className="px-5 py-3 font-medium">Actions</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((item) => (
                <tr key={item.id} className="border-t border-slate-100">
                  <td className="px-5 py-3 font-medium">{item.patientName}</td>
                  <td className="px-5 py-3">{item.patientContact || "—"}</td>
                  <td className="px-5 py-3">{item.doctorName}</td>
                  <td className="max-w-xs px-5 py-3 text-slate-600">{item.notes || "Clinic visit"}</td>
                  <td className="px-5 py-3">{formatDay(item.scheduledAt)} {formatTime(item.scheduledAt)}</td>
                  <td className="px-5 py-3">
                    {canManage ? (
                      <select className="w-auto" value={item.status} onChange={(e) => void setStatus(item.id, e.target.value)}>
                        {[...new Set([...STATUSES, item.status])].map((status) => (
                          <option key={status} value={status}>
                            {status === "Scheduled" ? "Confirmed" : status}
                          </option>
                        ))}
                      </select>
                    ) : (
                      <StatusBadge status={item.status} />
                    )}
                  </td>
                  <td className="px-5 py-3">
                    <button className="btn-ghost px-2" onClick={() => setDetail(item)} aria-label="View appointment">
                      <Eye className="h-4 w-4" />
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="card p-5">
        <h2 className="font-display text-xl">Upcoming — next 30 days</h2>
        <div className="mt-4 space-y-2">
          {upcoming.map((item) => (
            <div key={item.id} className="flex flex-wrap items-center justify-between gap-3 rounded-2xl bg-slate-50 px-4 py-3">
              <div>
                <p className="text-xs font-semibold uppercase text-teal-700">{formatDay(item.scheduledAt)}</p>
                <p className="font-medium">{item.patientName} · {formatTime(item.scheduledAt)}</p>
                <p className="text-xs text-slate-500">Booked with {item.doctorName} · {item.patientContact || "No number"}</p>
              </div>
              <StatusBadge status={item.status} />
            </div>
          ))}
        </div>
      </section>

      {openBook && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/40 p-4">
          <form onSubmit={book} className="card w-full max-w-lg space-y-4 p-5">
            <div className="flex items-center justify-between">
              <h2 className="font-display text-xl">Book Appointment</h2>
              <button type="button" onClick={() => setOpenBook(false)}><X className="h-5 w-5" /></button>
            </div>
            {error && <p className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>}
            <div>
              <label>Patient name</label>
              <select value={form.patientId} onChange={(e) => setForm({ ...form, patientId: Number(e.target.value) })}>
                {patients.map((p) => <option key={p.id} value={p.id}>{p.name}</option>)}
              </select>
            </div>
            <div>
              <label>Contact number</label>
              <input readOnly value={patients.find((p) => p.id === form.patientId)?.contact || "—"} />
            </div>
            <div>
              <label>Booked with</label>
              <select value={form.doctorId} onChange={(e) => setForm({ ...form, doctorId: Number(e.target.value) })}>
                {doctors.map((d) => <option key={d.id} value={d.id}>{d.name} · {d.specialization}</option>)}
              </select>
            </div>
            <div>
              <label>Date & time</label>
              <input type="datetime-local" value={form.scheduledAt} onChange={(e) => setForm({ ...form, scheduledAt: e.target.value })} required />
            </div>
            <div>
              <label>Call summary / notes</label>
              <textarea value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} placeholder="Any notes..." rows={3} />
            </div>
            <div className="flex justify-end gap-2">
              <button type="button" className="btn-ghost" onClick={() => setOpenBook(false)}>Cancel</button>
              <button className="btn-primary">Book Appointment</button>
            </div>
          </form>
        </div>
      )}

      {detail && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/40 p-4">
          <div className="card w-full max-w-md space-y-4 p-5">
            <div className="flex items-center justify-between">
              <h2 className="font-display text-xl">{detail.patientName}</h2>
              <button onClick={() => setDetail(null)}><X className="h-5 w-5" /></button>
            </div>
            <p className="text-sm text-slate-500">{formatDay(detail.scheduledAt)} · {formatTime(detail.scheduledAt)}</p>
            <StatusBadge status={detail.status} />
            <p className="text-sm"><span className="text-slate-500">Contact number:</span> {detail.patientContact || "—"}</p>
            <p className="text-sm"><span className="text-slate-500">Booked with:</span> {detail.doctorName}</p>
            <p className="rounded-2xl bg-slate-50 px-4 py-3 text-sm text-slate-700">{detail.notes || "No call summary."}</p>
            {canManage && detail.status === "Scheduled" && (
              <div className="flex gap-2">
                <button className="btn-primary" onClick={() => void setStatus(detail.id, "Completed").then(() => setDetail(null))}>Mark complete</button>
                <button className="btn-ghost" onClick={() => void setStatus(detail.id, "Cancelled").then(() => setDetail(null))}>Cancel</button>
              </div>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

function Metric({ label, value, className }: { label: string; value: number; className: string }) {
  return (
    <div className={`rounded-2xl p-4 ${className}`}>
      <p className="text-sm text-slate-600">{label}</p>
      <p className="mt-1 font-display text-3xl">{value}</p>
    </div>
  );
}

function PatientAppointments({
  appointments,
  patient,
  email,
  message,
  error,
  onRefresh,
  onCancel,
  onReschedule,
}: {
  appointments: Appointment[];
  patient?: Patient;
  email: string;
  message: string;
  error: string;
  onRefresh: () => void;
  onCancel: (id: number) => Promise<void>;
  onReschedule: (id: number, scheduledAt: string) => Promise<void>;
}) {
  const [openId, setOpenId] = useState<number | null>(appointments[0]?.id ?? null);
  const [rescheduleId, setRescheduleId] = useState<number | null>(null);
  const [when, setWhen] = useState(toInputValue(new Date(Date.now() + 60 * 60 * 1000)));
  const [busy, setBusy] = useState(false);
  const upcoming = appointments.filter((item) => item.status !== "Cancelled" && item.status !== "Completed" && item.status !== "No Show" && new Date(item.scheduledAt) >= new Date());
  const history = appointments.filter((item) => !upcoming.some((row) => row.id === item.id));

  function canManage(item: Appointment) {
    return item.status !== "Cancelled" && item.status !== "Completed" && item.status !== "No Show";
  }

  async function submitReschedule() {
    if (!rescheduleId) return;
    setBusy(true);
    try {
      await onReschedule(rescheduleId, when);
      setRescheduleId(null);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div className="space-y-8">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 className="font-display text-4xl text-slate-900">My Appointments</h2>
          <p className="mt-1 text-sm text-slate-500">{email}</p>
        </div>
        <button className="btn-ghost" onClick={onRefresh}>Refresh</button>
      </div>
      {message && <p className="rounded-xl bg-teal-50 px-3 py-2 text-sm text-teal-800">{message}</p>}
      {error && <p className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>}

      <section>
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-teal-700">Upcoming — {upcoming.length}</p>
        <div className="mt-3 space-y-3">
          {upcoming.length === 0 ? (
            <p className="card px-5 py-8 text-sm text-slate-500">No upcoming visits.</p>
          ) : (
            upcoming.map((item) => {
              const open = openId === item.id;
              return (
                <article key={item.id} className="card p-5">
                  <button type="button" className="flex w-full items-start gap-4 text-left" onClick={() => setOpenId(open ? null : item.id)}>
                    <div className="rounded-xl border border-slate-200 px-3 py-2 text-center">
                      <p className="text-[11px] font-semibold uppercase text-slate-400">{formatMonthDay(item.scheduledAt).split(" ")[0]}</p>
                      <p className="font-display text-xl text-slate-800">{new Date(item.scheduledAt).getDate()}</p>
                    </div>
                    <div className="min-w-0 flex-1">
                      <StatusBadge status={item.status} />
                      <h3 className="mt-2 font-display text-xl">{item.notes || "Clinic visit"}</h3>
                      <p className="mt-1 flex flex-wrap items-center gap-3 text-sm text-slate-500">
                        <span className="inline-flex items-center gap-1"><Clock3 className="h-3.5 w-3.5" />{formatTime(item.scheduledAt)}</span>
                        <span>{formatLongDay(item.scheduledAt)}</span>
                      </p>
                    </div>
                    <ChevronDown className={`mt-1 h-5 w-5 text-slate-400 transition ${open ? "rotate-180" : ""}`} />
                  </button>
                  {canManage(item) && (
                    <div className="mt-4 flex flex-wrap gap-2">
                      <button
                        className="rounded-xl bg-violet-600 px-4 py-2.5 text-sm font-semibold text-white hover:bg-violet-700"
                        onClick={() => {
                          setRescheduleId(item.id);
                          setWhen(toInputValue(new Date(item.scheduledAt)));
                        }}
                      >
                        Reschedule
                      </button>
                      <button
                        className="rounded-xl bg-rose-600 px-4 py-2.5 text-sm font-semibold text-white hover:bg-rose-700"
                        onClick={() => {
                          if (window.confirm("Cancel this appointment?")) void onCancel(item.id);
                        }}
                      >
                        Cancel
                      </button>
                    </div>
                  )}
                  {open && (
                    <div className="mt-4 space-y-3 border-t border-slate-100 pt-4">
                      <p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-400">Appointment details</p>
                      <div className="grid gap-3 text-sm sm:grid-cols-2">
                        <p className="inline-flex items-center gap-2"><Phone className="h-4 w-4 text-teal-700" />{item.patientContact || patient?.contact || "—"}</p>
                        <p className="inline-flex items-center gap-2"><Mail className="h-4 w-4 text-teal-700" />{email || patient?.email || "—"}</p>
                        <p><span className="text-slate-500">Booked with:</span> {item.doctorName}</p>
                        <p><span className="text-slate-500">Age:</span> {patient?.age || item.patientAge || "—"}</p>
                      </div>
                      <p className="rounded-2xl bg-slate-50 px-4 py-3 text-sm text-slate-700">{item.notes || "No call summary."}</p>
                    </div>
                  )}
                </article>
              );
            })
          )}
        </div>
      </section>

      <section>
        <p className="text-xs font-semibold uppercase tracking-[0.18em] text-slate-500">History — {history.length}</p>
        <div className="mt-3 space-y-3">
          {history.map((item) => {
            const open = openId === item.id;
            return (
              <article key={item.id} className="card p-4">
                <button type="button" className="flex w-full items-start gap-4 text-left" onClick={() => setOpenId(open ? null : item.id)}>
                  <div className={`rounded-xl border px-3 py-2 text-center ${item.status === "Cancelled" ? "border-rose-200" : "border-slate-200"}`}>
                    <p className="text-[11px] font-semibold uppercase text-slate-400">{formatMonthDay(item.scheduledAt).split(" ")[0]}</p>
                    <p className={`font-display text-xl ${item.status === "Cancelled" ? "text-rose-700" : "text-slate-800"}`}>{new Date(item.scheduledAt).getDate()}</p>
                  </div>
                  <div className="min-w-0 flex-1">
                    <StatusBadge status={item.status} />
                    <h3 className="mt-2 font-medium">{item.notes || "Clinic visit"}</h3>
                    <p className="text-sm text-slate-500">{formatTime(item.scheduledAt)} · {formatLongDay(item.scheduledAt)}</p>
                  </div>
                  <ChevronDown className={`mt-1 h-5 w-5 text-slate-400 transition ${open ? "rotate-180" : ""}`} />
                </button>
                {open && (
                  <div className="mt-3 grid gap-2 text-sm sm:grid-cols-2">
                    <p>Contact number: {item.patientContact || "—"}</p>
                    <p>Booked with: {item.doctorName}</p>
                    <p className="sm:col-span-2 rounded-2xl bg-slate-50 px-4 py-3 text-slate-700">{item.notes || "No call summary."}</p>
                  </div>
                )}
              </article>
            );
          })}
        </div>
      </section>

      {rescheduleId !== null && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-950/40 p-4">
          <div className="card w-full max-w-md space-y-4 p-5">
            <div className="flex items-center justify-between">
              <h2 className="font-display text-xl">Reschedule</h2>
              <button type="button" onClick={() => setRescheduleId(null)}><X className="h-5 w-5" /></button>
            </div>
            <p className="text-sm text-slate-500">Pick a new date and time. It must fall in the doctor's working hours.</p>
            <div>
              <label>Date & time</label>
              <input type="datetime-local" value={when} onChange={(e) => setWhen(e.target.value)} required />
            </div>
            <div className="flex justify-end gap-2">
              <button type="button" className="btn-ghost" onClick={() => setRescheduleId(null)}>Close</button>
              <button className="btn-primary" disabled={busy} onClick={() => void submitReschedule()}>
                {busy ? "Saving..." : "Save new time"}
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
