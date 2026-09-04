import { useEffect, useState, type FormEvent } from "react";
import { api, type InsuranceClaim, type InsurancePolicy, type Invoice, type Patient } from "../api/client";
import { useAuth } from "../context/AuthContext";

function money(value: number) {
  return `₹${value.toLocaleString("en-IN")}`;
}

function isoDate(value: Date) {
  const pad = (n: number) => String(n).padStart(2, "0");
  return `${value.getFullYear()}-${pad(value.getMonth() + 1)}-${pad(value.getDate())}`;
}

const claimFlow = ["Draft", "Submitted", "Under review", "Approved", "Rejected", "Paid"];

export function InsurancePage() {
  const { user } = useAuth();
  const canEdit = user?.role === "Admin" || user?.role === "Doctor";
  const [policies, setPolicies] = useState<InsurancePolicy[]>([]);
  const [claims, setClaims] = useState<InsuranceClaim[]>([]);
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [patients, setPatients] = useState<Patient[]>([]);
  const [error, setError] = useState("");
  const [policy, setPolicy] = useState({
    patientId: 0,
    provider: "",
    policyNumber: "",
    tpa: "",
    memberId: "",
    validFrom: isoDate(new Date()),
    validTo: isoDate(new Date(Date.now() + 365 * 24 * 60 * 60 * 1000)),
    coverageLimit: 500000,
  });
  const [claim, setClaim] = useState({ policyId: 0, invoiceId: "", amount: 0, notes: "" });

  async function load() {
    const [pol, clm, inv, pat] = await Promise.all([
      api.get<InsurancePolicy[]>("/insurance/policies"),
      api.get<InsuranceClaim[]>("/insurance/claims"),
      api.get<Invoice[]>("/billing/invoices"),
      api.get<Patient[]>("/patients"),
    ]);
    setPolicies(pol.data);
    setClaims(clm.data);
    setInvoices(inv.data);
    setPatients(pat.data);
    setPolicy((current) => ({ ...current, patientId: current.patientId || pat.data[0]?.id || 0 }));
    setClaim((current) => ({ ...current, policyId: current.policyId || pol.data[0]?.id || 0 }));
  }

  useEffect(() => {
    void load();
  }, []);

  async function savePolicy(e: FormEvent) {
    e.preventDefault();
    setError("");
    try {
      await api.post("/insurance/policies", policy);
      setPolicy((current) => ({ ...current, provider: "", policyNumber: "", tpa: "", memberId: "" }));
      await load();
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setError(axiosErr.response?.data?.message ?? "Could not save policy.");
    }
  }

  async function saveClaim(e: FormEvent) {
    e.preventDefault();
    await api.post("/insurance/claims", {
      ...claim,
      invoiceId: claim.invoiceId ? Number(claim.invoiceId) : null,
    });
    setClaim((current) => ({ ...current, amount: 0, notes: "", invoiceId: "" }));
    await load();
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="font-display text-2xl">Insurance / Claims Management</h2>
        <p className="text-sm text-slate-500">Store policies, run eligibility, submit claims, and track TPA status.</p>
      </div>

      {canEdit && (
        <div className="grid gap-6 xl:grid-cols-2">
          <form onSubmit={savePolicy} className="card space-y-3 p-5">
            <h3 className="font-display text-xl">Add policy</h3>
            {error && <p className="text-sm text-rose-600">{error}</p>}
            <div>
              <label>Patient</label>
              <select value={policy.patientId} onChange={(e) => setPolicy({ ...policy, patientId: Number(e.target.value) })}>
                {patients.map((patient) => (
                  <option key={patient.id} value={patient.id}>
                    {patient.uhid} · {patient.name}
                  </option>
                ))}
              </select>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label>Insurer</label>
                <input value={policy.provider} onChange={(e) => setPolicy({ ...policy, provider: e.target.value })} required />
              </div>
              <div>
                <label>Policy no.</label>
                <input value={policy.policyNumber} onChange={(e) => setPolicy({ ...policy, policyNumber: e.target.value })} required />
              </div>
            </div>
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label>TPA</label>
                <input value={policy.tpa} onChange={(e) => setPolicy({ ...policy, tpa: e.target.value })} />
              </div>
              <div>
                <label>Member ID</label>
                <input value={policy.memberId} onChange={(e) => setPolicy({ ...policy, memberId: e.target.value })} />
              </div>
            </div>
            <div className="grid grid-cols-3 gap-3">
              <div>
                <label>From</label>
                <input type="date" value={policy.validFrom} onChange={(e) => setPolicy({ ...policy, validFrom: e.target.value })} />
              </div>
              <div>
                <label>To</label>
                <input type="date" value={policy.validTo} onChange={(e) => setPolicy({ ...policy, validTo: e.target.value })} />
              </div>
              <div>
                <label>Cover (₹)</label>
                <input type="number" value={policy.coverageLimit} onChange={(e) => setPolicy({ ...policy, coverageLimit: Number(e.target.value) })} />
              </div>
            </div>
            <button className="btn-primary">Save policy</button>
          </form>

          <form onSubmit={saveClaim} className="card space-y-3 p-5">
            <h3 className="font-display text-xl">Submit claim</h3>
            <div>
              <label>Policy</label>
              <select value={claim.policyId} onChange={(e) => setClaim({ ...claim, policyId: Number(e.target.value) })}>
                {policies.map((item) => (
                  <option key={item.id} value={item.id}>
                    {item.patientName} · {item.provider} {item.policyNumber}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label>Invoice</label>
              <select value={claim.invoiceId} onChange={(e) => setClaim({ ...claim, invoiceId: e.target.value })}>
                <option value="">No invoice</option>
                {invoices.map((invoice) => (
                  <option key={invoice.id} value={invoice.id}>
                    {invoice.number} · {invoice.patientName} · {money(invoice.total)}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label>Claim amount (₹)</label>
              <input type="number" value={claim.amount} onChange={(e) => setClaim({ ...claim, amount: Number(e.target.value) })} required />
            </div>
            <div>
              <label>Notes</label>
              <input value={claim.notes} onChange={(e) => setClaim({ ...claim, notes: e.target.value })} />
            </div>
            <button className="btn-primary">Submit claim</button>
          </form>
        </div>
      )}

      <section className="card overflow-hidden">
        <div className="border-b border-slate-100 px-5 py-4">
          <h3 className="font-display text-xl">Policies</h3>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-500">
              <tr>
                <th className="px-5 py-3 font-medium">Patient</th>
                <th className="px-5 py-3 font-medium">Policy</th>
                <th className="px-5 py-3 font-medium">Validity</th>
                <th className="px-5 py-3 font-medium">Eligibility</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {policies.map((item) => (
                <tr key={item.id} className="border-t border-slate-100">
                  <td className="px-5 py-3 font-medium">{item.patientName}</td>
                  <td className="px-5 py-3">
                    {item.provider} · {item.policyNumber}
                    <div className="text-xs text-slate-500">
                      TPA {item.tpa || "—"} · Cover {money(item.coverageLimit)}
                    </div>
                  </td>
                  <td className="px-5 py-3">
                    {new Date(item.validFrom).toLocaleDateString()} – {new Date(item.validTo).toLocaleDateString()}
                  </td>
                  <td className="px-5 py-3">
                    {item.eligibility}
                    {item.eligibilityCheckedAt && (
                      <div className="text-xs text-slate-500">{new Date(item.eligibilityCheckedAt).toLocaleString()}</div>
                    )}
                  </td>
                  <td className="px-5 py-3 text-right">
                    {canEdit && (
                      <>
                        <button
                          className="text-teal-700 hover:underline"
                          onClick={() => void api.post(`/insurance/policies/${item.id}/eligibility`, { status: "Eligible" }).then(() => load())}
                        >
                          Eligible
                        </button>
                        <button
                          className="ml-3 text-rose-600 hover:underline"
                          onClick={() => void api.post(`/insurance/policies/${item.id}/eligibility`, { status: "Not eligible" }).then(() => load())}
                        >
                          Not eligible
                        </button>
                      </>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>

      <section className="card overflow-hidden">
        <div className="border-b border-slate-100 px-5 py-4">
          <h3 className="font-display text-xl">Claims tracking</h3>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-500">
              <tr>
                <th className="px-5 py-3 font-medium">Claim</th>
                <th className="px-5 py-3 font-medium">Patient / Policy</th>
                <th className="px-5 py-3 font-medium">Invoice</th>
                <th className="px-5 py-3 font-medium">Amount</th>
                <th className="px-5 py-3 font-medium">Status</th>
              </tr>
            </thead>
            <tbody>
              {claims.map((item) => (
                <tr key={item.id} className="border-t border-slate-100">
                  <td className="px-5 py-3 font-medium">{item.claimNumber}</td>
                  <td className="px-5 py-3">
                    {item.patientName}
                    <div className="text-xs text-slate-500">
                      {item.provider} · {item.policyNumber}
                    </div>
                  </td>
                  <td className="px-5 py-3">{item.invoiceNumber ?? "—"}</td>
                  <td className="px-5 py-3">{money(item.amount)}</td>
                  <td className="px-5 py-3">
                    {canEdit ? (
                      <select
                        className="w-auto"
                        value={item.status}
                        onChange={(e) => void api.patch(`/insurance/claims/${item.id}`, { status: e.target.value }).then(() => load())}
                      >
                        {claimFlow.map((status) => (
                          <option key={status}>{status}</option>
                        ))}
                      </select>
                    ) : (
                      item.status
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </section>
    </div>
  );
}
