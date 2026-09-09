import { useEffect, useMemo, useState, type FormEvent } from "react";
import { ArrowLeft, ChevronDown, FolderOpen, Stethoscope } from "lucide-react";
import { api, type Patient, type PatientDetail } from "../api/client";
import { PatientChart } from "../components/PatientChart";
import { StatusBadge } from "../components/StatusBadge";
import { formatStamp } from "../lib/format";
import { useAuth } from "../context/AuthContext";

const empty = {
  name: "",
  age: 0,
  contact: "",
  email: "",
  address: "",
  notes: "",
  gender: "",
  bloodGroup: "",
  emergencyName: "",
  emergencyPhone: "",
  allergies: "",
  password: "",
};

function initials(name: string) {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((part) => part[0]?.toUpperCase())
    .join("");
}

export function PatientsPage() {
  const { user } = useAuth();
  const canEdit = user?.role === "Admin" || user?.role === "Doctor";
  const canDelete = user?.role === "Admin";
  const [patients, setPatients] = useState<Patient[]>([]);
  const [form, setForm] = useState(empty);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [selectedId, setSelectedId] = useState<number | null>(null);
  const [detail, setDetail] = useState<PatientDetail | null>(null);
  const [query, setQuery] = useState("");
  const [error, setError] = useState("");
  const [expandedId, setExpandedId] = useState<number | null>(null);
  const [detailsById, setDetailsById] = useState<Record<number, PatientDetail>>({});

  async function load() {
    const { data } = await api.get<Patient[]>("/patients");
    setPatients(data);
  }

  async function loadDetail(id: number) {
    const { data } = await api.get<PatientDetail>(`/patients/${id}/details`);
    setDetail(data);
  }

  useEffect(() => {
    void load();
  }, []);

  useEffect(() => {
    if (selectedId) {
      void loadDetail(selectedId);
    } else {
      setDetail(null);
    }
  }, [selectedId]);

  async function toggleExpand(id: number) {
    if (expandedId === id) {
      setExpandedId(null);
      return;
    }
    setExpandedId(id);
    if (!detailsById[id]) {
      const { data } = await api.get<PatientDetail>(`/patients/${id}/details`);
      setDetailsById((current) => ({ ...current, [id]: data }));
    }
  }

  const filtered = useMemo(() => {
    const term = query.trim().toLowerCase();
    if (!term) return patients;
    return patients.filter(
      (patient) =>
        patient.name.toLowerCase().includes(term) ||
        patient.uhid.toLowerCase().includes(term) ||
        patient.contact.toLowerCase().includes(term) ||
        patient.email.toLowerCase().includes(term),
    );
  }, [patients, query]);

  async function save(e: FormEvent) {
    e.preventDefault();
    setError("");
    try {
      if (editingId) {
        await api.put(`/patients/${editingId}`, form);
      } else {
        await api.post("/patients", form);
      }
      setForm(empty);
      setEditingId(null);
      await load();
      if (selectedId) {
        await loadDetail(selectedId);
      }
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setError(axiosErr.response?.data?.message ?? "Could not save patient.");
    }
  }

  async function remove(id: number) {
    await api.delete(`/patients/${id}`);
    if (selectedId === id) {
      setSelectedId(null);
    }
    await load();
  }

  if (selectedId && (!detail || detail.patient.id !== selectedId)) {
    return <p className="text-slate-500">Loading patient…</p>;
  }

  if (selectedId && detail) {
    const { patient, visits, documents } = detail;
    return (
      <div className="space-y-6">
        <button className="btn-ghost" onClick={() => setSelectedId(null)}>
          <ArrowLeft className="h-4 w-4" />
          Back to patients
        </button>

        <section className="card p-5">
          <div className="flex flex-wrap items-start justify-between gap-4">
            <div className="flex items-center gap-4">
              <div className="flex h-14 w-14 items-center justify-center rounded-2xl bg-teal-100 font-display text-xl text-teal-800">
                {initials(patient.name)}
              </div>
              <div>
                <h2 className="font-display text-2xl">{patient.name}</h2>
                    <p className="text-sm text-slate-500">
                      {patient.uhid || "UHID pending"} · {patient.age ? `${patient.age} years` : "Age not set"} · {patient.contact}
                    </p>
                    <p className="text-sm text-slate-500">{patient.email || "No email"}</p>
              </div>
            </div>
            <div className="flex gap-2">
              {canEdit && (
                <button
                  className="btn-ghost"
                  onClick={() => {
                    setEditingId(patient.id);
                    setForm({
                      name: patient.name,
                      age: patient.age,
                      contact: patient.contact,
                      email: patient.email,
                      address: patient.address,
                      notes: patient.notes,
                      gender: patient.gender,
                      bloodGroup: patient.bloodGroup,
                      emergencyName: patient.emergencyName,
                      emergencyPhone: patient.emergencyPhone,
                      allergies: patient.allergies,
                      password: "",
                    });
                    setSelectedId(null);
                  }}
                >
                  Edit
                </button>
              )}
              {canDelete && (
                <button className="btn-danger" onClick={() => void remove(patient.id)}>
                  Delete
                </button>
              )}
            </div>
          </div>
          <div className="mt-5 grid gap-3 sm:grid-cols-2 lg:grid-cols-4">
            <DetailChip label="UHID" value={patient.uhid || "—"} />
            <DetailChip label="Gender / Blood" value={`${patient.gender || "—"} · ${patient.bloodGroup || "—"}`} />
            <DetailChip label="Allergies" value={patient.allergies || "None recorded"} />
            <DetailChip label="Emergency" value={patient.emergencyName ? `${patient.emergencyName} · ${patient.emergencyPhone}` : "—"} />
            <DetailChip label="Address" value={patient.address || "—"} />
            <DetailChip label="Notes" value={patient.notes || "—"} />
            <DetailChip label="Visits" value={String(patient.visitCount)} />
            <DetailChip label="Documents" value={String(patient.documentCount)} />
          </div>
        </section>

        <PatientChart
          patient={patient}
          visits={visits}
          documents={documents}
          charges={detail.charges}
          canBill={canEdit}
          onChanged={async () => {
            await Promise.all([load(), loadDetail(patient.id)]);
          }}
        />
      </div>
    );
  }

  return (
    <div className="grid gap-6 xl:grid-cols-[380px_1fr]">
      {canEdit && (
        <form onSubmit={save} className="card h-fit space-y-3 p-5">
          <h2 className="font-display text-xl">{editingId ? "Edit patient" : "Register patient"}</h2>
          {error && <p className="text-sm text-rose-600">{error}</p>}
          <div>
            <label>Name</label>
            <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label>Age</label>
              <input type="number" value={form.age} onChange={(e) => setForm({ ...form, age: Number(e.target.value) })} />
            </div>
            <div>
              <label>Contact</label>
              <input value={form.contact} onChange={(e) => setForm({ ...form, contact: e.target.value })} required />
            </div>
          </div>
          <div>
            <label>Email</label>
            <input type="email" value={form.email} onChange={(e) => setForm({ ...form, email: e.target.value })} />
          </div>
          <div>
            <label>Portal password</label>
            <input
              type="password"
              value={form.password}
              onChange={(e) => setForm({ ...form, password: e.target.value })}
              placeholder={editingId ? "Leave blank to keep current" : "Set so they can sign in"}
              autoComplete="new-password"
            />
            <p className="mt-1 text-xs text-slate-500">They sign in with this email and password. Use at least 6 characters with upper, lower, and a number (example Patient@123).</p>
          </div>
          <div>
            <label>Address</label>
            <input value={form.address} onChange={(e) => setForm({ ...form, address: e.target.value })} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label>Gender</label>
              <select value={form.gender} onChange={(e) => setForm({ ...form, gender: e.target.value })}>
                <option value="">Select</option>
                <option>Male</option>
                <option>Female</option>
                <option>Other</option>
              </select>
            </div>
            <div>
              <label>Blood group</label>
              <input value={form.bloodGroup} onChange={(e) => setForm({ ...form, bloodGroup: e.target.value })} placeholder="B+" />
            </div>
          </div>
          <div>
            <label>Allergies</label>
            <input value={form.allergies} onChange={(e) => setForm({ ...form, allergies: e.target.value })} placeholder="None / penicillin" />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label>Emergency name</label>
              <input value={form.emergencyName} onChange={(e) => setForm({ ...form, emergencyName: e.target.value })} />
            </div>
            <div>
              <label>Emergency phone</label>
              <input value={form.emergencyPhone} onChange={(e) => setForm({ ...form, emergencyPhone: e.target.value })} />
            </div>
          </div>
          <div>
            <label>Notes</label>
            <textarea rows={3} value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} />
          </div>
          <div className="flex gap-2">
            <button className="btn-primary">{editingId ? "Update" : "Create"}</button>
            {editingId && (
              <button
                type="button"
                className="btn-ghost"
                onClick={() => {
                  setEditingId(null);
                  setForm(empty);
                }}
              >
                Cancel
              </button>
            )}
          </div>
        </form>
      )}

      <div className="space-y-4">
        <div className="card overflow-hidden">
          <div className="flex flex-wrap items-end justify-between gap-3 border-b border-slate-100 px-5 py-4">
            <div>
              <h2 className="font-display text-xl">Patient profiles</h2>
              <p className="text-sm text-slate-500">{patients.length} total · expand a row for booked with and call summary</p>
            </div>
            <input
              className="max-w-sm"
              placeholder="Search by name, email, phone..."
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          </div>

          {filtered.length === 0 ? (
            <p className="px-5 py-10 text-center text-slate-500">No patients match that search.</p>
          ) : (
            <div className="divide-y divide-slate-100">
              {filtered.map((patient) => {
                const open = expandedId === patient.id;
                const detailRow = detailsById[patient.id];
                const lastVisit = detailRow?.visits
                  .slice()
                  .sort((a, b) => +new Date(b.scheduledAt) - +new Date(a.scheduledAt))[0];
                return (
                  <article key={patient.id} className="px-5 py-4">
                    <button type="button" className="flex w-full items-start gap-4 text-left" onClick={() => void toggleExpand(patient.id)}>
                      <div className="flex h-12 w-12 shrink-0 items-center justify-center rounded-2xl bg-teal-100 font-display text-lg text-teal-800">
                        {initials(patient.name)}
                      </div>
                      <div className="min-w-0 flex-1">
                        <p className="font-display text-lg text-slate-900">{patient.name}</p>
                        <p className="text-sm text-slate-500">{patient.email || "No email"} · {patient.contact || "No number"}</p>
                        <div className="mt-2 flex flex-wrap gap-2 text-xs text-slate-500">
                          {patient.hasLogin && (
                            <span className="rounded-full bg-teal-700 px-2 py-1 font-semibold text-white">Portal login</span>
                          )}
                          <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-1">
                            <Stethoscope className="h-3 w-3" />
                            {patient.visitCount} appointments
                          </span>
                          <span className="inline-flex items-center gap-1 rounded-full bg-teal-50 px-2 py-1 text-teal-800">
                            <FolderOpen className="h-3 w-3" />
                            {patient.documentCount} docs
                          </span>
                          {lastVisit && <span>Last visit {formatStamp(lastVisit.scheduledAt)}</span>}
                        </div>
                      </div>
                      <ChevronDown className={`mt-1 h-5 w-5 shrink-0 text-slate-400 transition ${open ? "rotate-180" : ""}`} />
                    </button>

                    {open && (
                      <div className="mt-4 rounded-2xl bg-slate-50 p-4">
                        {!detailRow ? (
                          <p className="text-sm text-slate-500">Loading visit history…</p>
                        ) : (
                          <>
                            <div className="mb-4 grid gap-3 sm:grid-cols-2">
                              <p className="text-sm"><span className="text-slate-500">Contact number:</span> {patient.contact || "—"}</p>
                              <p className="text-sm"><span className="text-slate-500">Email:</span> {patient.email || "—"}</p>
                            </div>
                            <p className="mb-2 text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Appointment history</p>
                            {detailRow.visits.length === 0 ? (
                              <p className="text-sm text-slate-500">No visits yet.</p>
                            ) : (
                              <div className="overflow-x-auto rounded-xl bg-white">
                                <table className="min-w-full text-left text-sm">
                                  <thead className="text-slate-500">
                                    <tr>
                                      <th className="px-3 py-2 font-medium">Date & time</th>
                                      <th className="px-3 py-2 font-medium">Booked with</th>
                                      <th className="px-3 py-2 font-medium">Call summary</th>
                                      <th className="px-3 py-2 font-medium">Status</th>
                                    </tr>
                                  </thead>
                                  <tbody>
                                    {detailRow.visits.map((visit) => (
                                      <tr key={visit.id} className="border-t border-slate-100">
                                        <td className="px-3 py-2">{formatStamp(visit.scheduledAt)}</td>
                                        <td className="px-3 py-2">{visit.doctorName}</td>
                                        <td className="max-w-xs px-3 py-2 text-slate-600">{visit.notes || "—"}</td>
                                        <td className="px-3 py-2"><StatusBadge status={visit.status} /></td>
                                      </tr>
                                    ))}
                                  </tbody>
                                </table>
                              </div>
                            )}
                            <button className="btn-primary mt-4" onClick={() => setSelectedId(patient.id)}>
                              Open full chart
                            </button>
                          </>
                        )}
                      </div>
                    )}
                  </article>
                );
              })}
            </div>
          )}
        </div>
      </div>
    </div>
  );
}

function DetailChip({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl bg-slate-50 px-4 py-3">
      <p className="text-xs uppercase tracking-wide text-slate-500">{label}</p>
      <p className="mt-1 text-sm text-slate-800">{value}</p>
    </div>
  );
}
