import { useMemo, useState } from "react";
import { api, type Appointment, type Patient, type PatientCharge, type PatientDocument } from "../api/client";
import { PatientDocumentsPanel } from "./PatientDocuments";

function formatStamp(value: string) {
  return new Date(value).toLocaleString([], { dateStyle: "medium", timeStyle: "short" });
}

const chargeKinds = ["Billing", "Daily bill", "Insurance", "Payment"];

export function PatientChart({
  patient,
  visits,
  documents,
  charges,
  canBill,
  onChanged,
}: {
  patient: Patient;
  visits: Appointment[];
  documents: PatientDocument[];
  charges: PatientCharge[];
  canBill: boolean;
  onChanged: () => Promise<void> | void;
}) {
  const [tab, setTab] = useState<"overview" | "visits" | "records" | "billing">("overview");
  const [bill, setBill] = useState({ kind: "Daily bill", title: "", amount: 0, notes: "", appointmentId: "" });

  const timeline = useMemo(() => {
    const events = [
      ...visits.map((visit) => ({
        at: visit.scheduledAt,
        title: `${visit.status} visit with ${visit.doctorName}`,
        detail: visit.notes || "Appointment",
        kind: "Visit",
      })),
      ...documents.map((doc) => ({
        at: doc.uploadedAt,
        title: `${doc.category}: ${doc.originalName}`,
        detail: doc.appointmentId ? "Attached to a visit" : "Filed on patient chart",
        kind: "Record",
      })),
      ...charges.map((charge) => ({
        at: charge.chargeDate,
        title: `${charge.kind} · ₹${charge.amount.toLocaleString("en-IN")}`,
        detail: charge.title,
        kind: "Billing",
      })),
    ];
    return events.sort((a, b) => new Date(b.at).getTime() - new Date(a.at).getTime());
  }, [visits, documents, charges]);

  const due = charges.filter((c) => c.kind !== "Payment").reduce((sum, c) => sum + c.amount, 0);
  const paid = charges.filter((c) => c.kind === "Payment").reduce((sum, c) => sum + c.amount, 0);

  async function addCharge() {
    if (!bill.title || !bill.amount) return;
    await api.post(`/patients/${patient.id}/charges`, {
      kind: bill.kind,
      title: bill.title,
      amount: bill.amount,
      notes: bill.notes,
      appointmentId: bill.appointmentId ? Number(bill.appointmentId) : null,
    });
    setBill({ kind: "Daily bill", title: "", amount: 0, notes: "", appointmentId: "" });
    await onChanged();
  }

  const tabs = [
    { id: "overview" as const, label: "Overview" },
    { id: "visits" as const, label: `Visits (${visits.length})` },
    { id: "records" as const, label: `Records (${documents.length})` },
    { id: "billing" as const, label: `Billing (${charges.length})` },
  ];

  return (
    <div className="space-y-4">
      <div className="flex flex-wrap gap-2">
        {tabs.map((item) => (
          <button
            key={item.id}
            type="button"
            className={`rounded-full px-4 py-2 text-sm font-medium ${
              tab === item.id ? "bg-slate-900 text-white" : "bg-white text-slate-600 ring-1 ring-slate-200"
            }`}
            onClick={() => setTab(item.id)}
          >
            {item.label}
          </button>
        ))}
      </div>

      {tab === "overview" && (
        <section className="card p-5">
          <h3 className="font-display text-xl">Patient timeline</h3>
          <p className="mb-4 text-sm text-slate-500">Visits, records, and charges in one chart, the way clinic CRMs keep a file.</p>
          {timeline.length === 0 ? (
            <p className="text-sm text-slate-400">No chart activity yet.</p>
          ) : (
            <ol className="space-y-3">
              {timeline.map((event, index) => (
                <li key={`${event.kind}-${event.at}-${index}`} className="flex gap-3 rounded-2xl bg-slate-50 px-4 py-3">
                  <span className="mt-0.5 rounded-full bg-white px-2 py-0.5 text-xs font-semibold text-teal-800">{event.kind}</span>
                  <div>
                    <p className="text-sm font-medium">{event.title}</p>
                    <p className="text-xs text-slate-500">
                      {formatStamp(event.at)} · {event.detail}
                    </p>
                  </div>
                </li>
              ))}
            </ol>
          )}
        </section>
      )}

      {tab === "visits" && (
        <section className="card overflow-hidden">
          <div className="border-b border-slate-100 px-5 py-4">
            <h3 className="font-display text-xl">Encounters</h3>
            <p className="text-sm text-slate-500">Each visit is the parent for notes, files, and daily bills.</p>
          </div>
          {visits.length === 0 ? (
            <p className="px-5 py-8 text-sm text-slate-500">No previous visits yet.</p>
          ) : (
            <div className="overflow-x-auto">
              <table className="min-w-full text-left text-sm">
                <thead className="bg-slate-50 text-slate-500">
                  <tr>
                    <th className="px-5 py-3 font-medium">When</th>
                    <th className="px-5 py-3 font-medium">Doctor</th>
                    <th className="px-5 py-3 font-medium">Status</th>
                    <th className="px-5 py-3 font-medium">Linked records</th>
                    <th className="px-5 py-3 font-medium">Notes</th>
                  </tr>
                </thead>
                <tbody>
                  {visits.map((visit) => (
                    <tr key={visit.id} className="border-t border-slate-100">
                      <td className="px-5 py-3 font-medium">{formatStamp(visit.scheduledAt)}</td>
                      <td className="px-5 py-3">{visit.doctorName}</td>
                      <td className="px-5 py-3">
                        <span className="rounded-full bg-teal-50 px-2 py-1 text-xs font-semibold text-teal-800">{visit.status}</span>
                      </td>
                      <td className="px-5 py-3 text-slate-500">
                        {documents.filter((doc) => doc.appointmentId === visit.id).length} files ·{" "}
                        {charges.filter((charge) => charge.appointmentId === visit.id).length} charges
                      </td>
                      <td className="px-5 py-3 text-slate-500">{visit.notes || "—"}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </section>
      )}

      {tab === "records" && (
        <PatientDocumentsPanel patientId={patient.id} documents={documents} visits={visits} onChanged={onChanged} />
      )}

      {tab === "billing" && (
        <section className="card space-y-5 p-5">
          <div className="flex flex-wrap justify-between gap-3">
            <div>
              <h3 className="font-display text-xl">Billing ledger</h3>
              <p className="text-sm text-slate-500">Structured amounts, not only uploaded PDFs — daily bills, invoices, insurance, and payments.</p>
            </div>
            <div className="text-right text-sm">
              <p className="text-slate-500">Charged ₹{due.toLocaleString("en-IN")}</p>
              <p className="font-semibold text-teal-800">Paid ₹{paid.toLocaleString("en-IN")}</p>
            </div>
          </div>

          {canBill && (
            <div className="grid gap-3 rounded-2xl bg-slate-50 p-4 md:grid-cols-2 xl:grid-cols-5">
              <div>
                <label>Type</label>
                <select value={bill.kind} onChange={(e) => setBill({ ...bill, kind: e.target.value })}>
                  {chargeKinds.map((kind) => (
                    <option key={kind}>{kind}</option>
                  ))}
                </select>
              </div>
              <div>
                <label>Title</label>
                <input value={bill.title} onChange={(e) => setBill({ ...bill, title: e.target.value })} placeholder="OPD / ward / lab" />
              </div>
              <div>
                <label>Amount (₹)</label>
                <input type="number" value={bill.amount} onChange={(e) => setBill({ ...bill, amount: Number(e.target.value) })} />
              </div>
              <div>
                <label>Visit</label>
                <select value={bill.appointmentId} onChange={(e) => setBill({ ...bill, appointmentId: e.target.value })}>
                  <option value="">Not linked</option>
                  {visits.map((visit) => (
                    <option key={visit.id} value={visit.id}>
                      {new Date(visit.scheduledAt).toLocaleDateString()} · {visit.doctorName}
                    </option>
                  ))}
                </select>
              </div>
              <div className="flex items-end">
                <button className="btn-primary w-full" onClick={() => void addCharge()}>
                  Add line
                </button>
              </div>
            </div>
          )}

          {charges.length === 0 ? (
            <p className="text-sm text-slate-400">No billing lines yet. Upload a bill PDF in Records, or add a ledger line here.</p>
          ) : (
            <table className="min-w-full text-left text-sm">
              <thead className="text-slate-500">
                <tr>
                  <th className="py-2 font-medium">Date</th>
                  <th className="py-2 font-medium">Type</th>
                  <th className="py-2 font-medium">Particulars</th>
                  <th className="py-2 font-medium">Visit</th>
                  <th className="py-2 text-right font-medium">Amount</th>
                  {canBill && <th></th>}
                </tr>
              </thead>
              <tbody>
                {charges.map((charge) => (
                  <tr key={charge.id} className="border-t border-slate-100">
                    <td className="py-2">{new Date(charge.chargeDate).toLocaleDateString()}</td>
                    <td className="py-2">{charge.kind}</td>
                    <td className="py-2">{charge.title}</td>
                    <td className="py-2 text-slate-500">
                      {visits.find((visit) => visit.id === charge.appointmentId)?.doctorName ?? "—"}
                    </td>
                    <td className="py-2 text-right font-medium">₹{charge.amount.toLocaleString("en-IN")}</td>
                    {canBill && (
                      <td className="py-2 text-right">
                        <button
                          className="text-xs text-rose-600 hover:underline"
                          onClick={() => void api.delete(`/patients/${patient.id}/charges/${charge.id}`).then(() => onChanged())}
                        >
                          Remove
                        </button>
                      </td>
                    )}
                  </tr>
                ))}
              </tbody>
            </table>
          )}
        </section>
      )}
    </div>
  );
}
