import { useEffect, useState, type FormEvent } from "react";
import { api, type Appointment, type AppointmentPage, type Doctor, type Patient } from "../api/client";
import { useAuth } from "../context/AuthContext";

const PAGE_SIZE = 8;

function toInputValue(date: Date) {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())}T${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function AppointmentsPage() {
  const { user } = useAuth();
  const canManage = user?.role === "Admin" || user?.role === "Doctor";
  const isPatient = user?.role === "Patient";
  const isDoctor = user?.role === "Doctor";
  const [appointments, setAppointments] = useState<Appointment[]>([]);
  const [patients, setPatients] = useState<Patient[]>([]);
  const [doctors, setDoctors] = useState<Doctor[]>([]);
  const [patientId, setPatientId] = useState<number>(0);
  const [doctorId, setDoctorId] = useState<number>(0);
  const [scheduledAt, setScheduledAt] = useState(toInputValue(new Date(Date.now() + 60 * 60 * 1000)));
  const [notes, setNotes] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");
  const [schedulePage, setSchedulePage] = useState(1);
  const [scheduleTotal, setScheduleTotal] = useState(0);
  const [schedulePageSize, setSchedulePageSize] = useState(PAGE_SIZE);
  const me = patients[0];

  async function loadSchedule(page = schedulePage) {
    const { data } = await api.get<AppointmentPage>("/appointment", { params: { page, pageSize: PAGE_SIZE } });
    if (data.items.length === 0 && data.page > 1 && data.total > 0) {
      return loadSchedule(data.page - 1);
    }
    setAppointments(data.items);
    setSchedulePage(data.page);
    setScheduleTotal(data.total);
    setSchedulePageSize(data.pageSize);
  }

  async function load(page = schedulePage) {
    const [pat, doc] = await Promise.all([
      api.get<Patient[]>("/patients"),
      api.get<Doctor[]>("/doctors"),
    ]);
    await loadSchedule(page);
    setPatients(pat.data);
    const activeDoctors = doc.data.filter((d) => d.isActive);
    setDoctors(activeDoctors);
    setPatientId((current) => current || pat.data[0]?.id || 0);
    const mine = activeDoctors.find((d) => d.email.toLowerCase() === user?.email.toLowerCase());
    setDoctorId((current) => mine?.id || current || activeDoctors[0]?.id || 0);
  }

  useEffect(() => {
    void load();
  }, []);

  async function book(e: FormEvent) {
    e.preventDefault();
    setError("");
    setMessage("");
    try {
      await api.post("/appointment/book", {
        patientId,
        doctorId,
        scheduledAt: new Date(scheduledAt).toISOString(),
        notes,
      });
      setNotes("");
      setMessage("Appointment booked. Email sent to that doctor's email address.");
      await load(1);
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setError(axiosErr.response?.data?.message ?? "Booking failed.");
    }
  }

  async function setStatus(id: number, status: string) {
    await api.patch(`/appointment/${id}/status`, { status });
    await load();
  }

  const selectedDoctor = doctors.find((d) => d.id === doctorId);
  const days = ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

  return (
    <div className="space-y-6">
      {isPatient && me && (
        <section className="card p-5">
          <h2 className="font-display text-xl">Patient details</h2>
          <p className="mt-2 text-sm text-slate-600">
            {me.name} · Age {me.age || "—"} · {me.contact} · {me.email}
          </p>
        </section>
      )}
      <form onSubmit={book} className="card grid gap-4 p-5 lg:grid-cols-4">
        <div className="lg:col-span-4">
          <h2 className="font-display text-xl">{isPatient ? "Book your appointment" : "Appointment Scheduling"}</h2>
          <p className="text-sm text-slate-500">Availability is checked against doctor timings. A confirmation email is sent to that doctor's email address.</p>
        </div>
        {message && <p className="lg:col-span-4 rounded-xl bg-teal-50 px-3 py-2 text-sm text-teal-800">{message}</p>}
        {error && <p className="lg:col-span-4 rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>}
        {!isPatient && (
        <div>
          <label>Patient</label>
          <select value={patientId} onChange={(e) => setPatientId(Number(e.target.value))}>
            {patients.map((p) => (
              <option key={p.id} value={p.id}>
                {p.name}
              </option>
            ))}
          </select>
        </div>
        )}
        {!isDoctor && (
        <div>
          <label>Doctor</label>
          <select value={doctorId} onChange={(e) => setDoctorId(Number(e.target.value))}>
            {doctors.map((d) => (
              <option key={d.id} value={d.id}>
                {d.name} · {d.specialization}
              </option>
            ))}
          </select>
        </div>
        )}
        {isDoctor && selectedDoctor && (
          <div>
            <label>Doctor</label>
            <p className="rounded-xl border border-slate-200 bg-slate-50 px-3 py-2.5 text-sm">{selectedDoctor.name}</p>
          </div>
        )}
        <div>
          <label>Date & time</label>
          <input type="datetime-local" value={scheduledAt} onChange={(e) => setScheduledAt(e.target.value)} required />
        </div>
        <div>
          <label>Notes</label>
          <input value={notes} onChange={(e) => setNotes(e.target.value)} />
        </div>
        <div className="lg:col-span-3 text-sm text-slate-500">
          {selectedDoctor
            ? `Hours: ${selectedDoctor.schedules.map((s) => `${days[s.dayOfWeek]} ${s.startTime}-${s.endTime}`).join(" · ")}`
            : "Select a doctor to see availability."}
        </div>
        <button className="btn-primary">Book & notify doctor</button>
      </form>

      <div className="card overflow-hidden">
        <div className="border-b border-slate-100 px-5 py-4">
          <h2 className="font-display text-xl">Appointment schedule</h2>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-500">
              <tr>
                <th className="px-5 py-3 font-medium">When</th>
                <th className="px-5 py-3 font-medium">Patient</th>
                <th className="px-5 py-3 font-medium">Doctor</th>
                <th className="px-5 py-3 font-medium">Status</th>
                <th className="px-5 py-3 font-medium"></th>
              </tr>
            </thead>
            <tbody>
              {appointments.length === 0 ? (
                <tr>
                  <td colSpan={5} className="px-5 py-8 text-center text-slate-500">
                    No appointments on this page.
                  </td>
                </tr>
              ) : (
                appointments.map((item) => (
                  <tr key={item.id} className="border-t border-slate-100">
                    <td className="px-5 py-3">{new Date(item.scheduledAt).toLocaleString()}</td>
                    <td className="px-5 py-3">
                      {item.patientName}
                      <div className="text-xs text-slate-500">{item.patientContact}</div>
                    </td>
                    <td className="px-5 py-3">{item.doctorName}</td>
                    <td className="px-5 py-3">{item.status}</td>
                    <td className="px-5 py-3 text-right">
                      {canManage && item.status === "Scheduled" && (
                        <>
                          <button className="text-teal-700 hover:underline" onClick={() => void setStatus(item.id, "Completed")}>
                            Complete
                          </button>
                          <button className="ml-3 text-rose-600 hover:underline" onClick={() => void setStatus(item.id, "Cancelled")}>
                            Cancel
                          </button>
                        </>
                      )}
                    </td>
                  </tr>
                ))
              )}
            </tbody>
          </table>
        </div>
        {scheduleTotal > 0 && (
          <div className="flex flex-wrap items-center justify-between gap-3 border-t border-slate-100 px-5 py-3">
            <p className="text-sm text-slate-500">
              Showing {(schedulePage - 1) * schedulePageSize + 1}–
              {Math.min(schedulePage * schedulePageSize, scheduleTotal)} of {scheduleTotal}
            </p>
            <div className="flex items-center gap-2">
              <button
                className="btn-ghost"
                disabled={schedulePage <= 1}
                onClick={() => void loadSchedule(schedulePage - 1)}
              >
                Previous
              </button>
              <span className="text-sm text-slate-600">
                Page {schedulePage} of {Math.max(1, Math.ceil(scheduleTotal / schedulePageSize))}
              </span>
              <button
                className="btn-ghost"
                disabled={schedulePage * schedulePageSize >= scheduleTotal}
                onClick={() => void loadSchedule(schedulePage + 1)}
              >
                Next
              </button>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}
