import { useEffect, useMemo, useState, type FormEvent } from "react";
import { ArrowLeft, FolderOpen, Phone, Stethoscope } from "lucide-react";
import { api, type Patient, type PatientDetail } from "../api/client";
import { PatientChart } from "../components/PatientChart";
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
    } catch {
      setError("Could not save patient.");
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
        <div className="card p-5">
          <div className="flex flex-wrap items-end justify-between gap-3">
            <div>
              <h2 className="font-display text-xl">Patient Management</h2>
              <p className="text-sm text-slate-500">Click a card to open the patient chart — visits, records, and billing.</p>
            </div>
            <input
              className="max-w-xs"
              placeholder="Search UHID, name, phone, or email"
              value={query}
              onChange={(e) => setQuery(e.target.value)}
            />
          </div>
        </div>

        {filtered.length === 0 ? (
          <div className="card px-5 py-10 text-center text-slate-500">No patients match that search.</div>
        ) : (
          <div className="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
            {filtered.map((patient) => (
              <button
                key={patient.id}
                type="button"
                className="card p-5 text-left transition hover:-translate-y-0.5 hover:border-teal-200 hover:shadow-md"
                onClick={() => setSelectedId(patient.id)}
              >
                <div className="flex items-start gap-3">
                  <div className="flex h-12 w-12 items-center justify-center rounded-2xl bg-teal-100 font-display text-lg text-teal-800">
                    {initials(patient.name)}
                  </div>
                  <div className="min-w-0">
                    <p className="truncate font-display text-lg">{patient.name}</p>
                    <p className="text-sm text-slate-500">
                      {patient.uhid || "No UHID"} · {patient.age ? `${patient.age} yrs` : "Age not set"}
                    </p>
                  </div>
                </div>
                <p className="mt-4 flex items-center gap-2 text-sm text-slate-600">
                  <Phone className="h-3.5 w-3.5" />
                  {patient.contact}
                </p>
                <div className="mt-4 flex gap-2">
                  <span className="inline-flex items-center gap-1 rounded-full bg-slate-100 px-2 py-1 text-xs text-slate-600">
                    <Stethoscope className="h-3 w-3" />
                    {patient.visitCount} visits
                  </span>
                  <span className="inline-flex items-center gap-1 rounded-full bg-teal-50 px-2 py-1 text-xs text-teal-800">
                    <FolderOpen className="h-3 w-3" />
                    {patient.documentCount} docs
                  </span>
                </div>
              </button>
            ))}
          </div>
        )}
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
