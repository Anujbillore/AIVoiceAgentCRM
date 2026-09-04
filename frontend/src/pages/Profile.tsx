import { useEffect, useState, type FormEvent } from "react";
import { api, type PatientDetail } from "../api/client";
import { PatientChart } from "../components/PatientChart";

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

export function ProfilePage() {
  const [form, setForm] = useState(empty);
  const [detail, setDetail] = useState<PatientDetail | null>(null);
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  async function load() {
    const { data } = await api.get<PatientDetail>("/patients/me/details");
    setDetail(data);
    const patient = data.patient;
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
  }

  useEffect(() => {
    void load();
  }, []);

  async function save(e: FormEvent) {
    e.preventDefault();
    setError("");
    setMessage("");
    try {
      await api.put("/patients/me", form);
      setMessage("Your details were saved.");
      await load();
    } catch {
      setError("Could not update your profile.");
    }
  }

  if (!detail) {
    return <p className="text-slate-500">Loading your details…</p>;
  }

  const { patient, visits, documents } = detail;

  return (
    <div className="mx-auto grid max-w-5xl gap-6">
      <form onSubmit={save} className="card space-y-3 p-6">
        <div>
          <h2 className="font-display text-xl">Patient details</h2>
          <p className="text-sm text-slate-500">
          Your clinic chart ID is {detail.patient.uhid || "assigned after save"}. Keep contact and allergy details current.
        </p>
        </div>
        {message && <p className="rounded-xl bg-teal-50 px-3 py-2 text-sm text-teal-800">{message}</p>}
        {error && <p className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>}
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
            <input value={form.bloodGroup} onChange={(e) => setForm({ ...form, bloodGroup: e.target.value })} />
          </div>
        </div>
        <div>
          <label>Allergies</label>
          <input value={form.allergies} onChange={(e) => setForm({ ...form, allergies: e.target.value })} />
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
        <button className="btn-primary">Save details</button>
      </form>

      <PatientChart
        patient={patient}
        visits={visits}
        documents={documents}
        charges={detail.charges}
        canBill={false}
        onChanged={load}
      />
    </div>
  );
}
