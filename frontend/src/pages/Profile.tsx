import { useEffect, useState } from "react";
import { api, type PatientDetail } from "../api/client";
import { PatientChart } from "../components/PatientChart";

export function ProfilePage() {
  const [detail, setDetail] = useState<PatientDetail | null>(null);

  useEffect(() => {
    void api.get<PatientDetail>("/patients/me/details").then((res) => setDetail(res.data));
  }, []);

  if (!detail) {
    return <p className="text-slate-500">Loading your details…</p>;
  }

  const { patient, visits, documents } = detail;

  return (
    <div className="mx-auto grid max-w-5xl gap-6">
      <section className="card space-y-4 p-6">
        <div>
          <h2 className="font-display text-xl">My profile</h2>
          <p className="text-sm text-slate-500">Clinic chart ID {detail.patient.uhid || "pending"}. Contact the desk to update these details.</p>
        </div>
        <div className="grid gap-3 sm:grid-cols-2">
          <ReadRow label="Name" value={patient.name} />
          <ReadRow label="Age" value={patient.age ? String(patient.age) : "—"} />
          <ReadRow label="Contact" value={patient.contact || "—"} />
          <ReadRow label="Email" value={patient.email || "—"} />
          <ReadRow label="Address" value={patient.address || "—"} />
          <ReadRow label="Gender / blood" value={`${patient.gender || "—"} · ${patient.bloodGroup || "—"}`} />
          <ReadRow label="Allergies" value={patient.allergies || "None recorded"} />
          <ReadRow label="Emergency" value={patient.emergencyName ? `${patient.emergencyName} · ${patient.emergencyPhone}` : "—"} />
        </div>
      </section>

      <PatientChart
        patient={patient}
        visits={visits}
        documents={documents}
        charges={detail.charges}
        canBill={false}
        onChanged={async () => {
          const { data } = await api.get<PatientDetail>("/patients/me/details");
          setDetail(data);
        }}
      />
    </div>
  );
}

function ReadRow({ label, value }: { label: string; value: string }) {
  return (
    <div className="rounded-2xl bg-slate-50 px-4 py-3">
      <p className="text-xs uppercase tracking-wide text-slate-500">{label}</p>
      <p className="mt-1 text-sm text-slate-800">{value}</p>
    </div>
  );
}
