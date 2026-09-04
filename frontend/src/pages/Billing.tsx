import { useEffect, useState, type FormEvent } from "react";
import { api, type Invoice, type Patient } from "../api/client";
import { useAuth } from "../context/AuthContext";

function money(value: number) {
  return `₹${value.toLocaleString("en-IN", { maximumFractionDigits: 2 })}`;
}

export function BillingPage() {
  const { user } = useAuth();
  const canEdit = user?.role === "Admin" || user?.role === "Doctor";
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [patients, setPatients] = useState<Patient[]>([]);
  const [error, setError] = useState("");
  const [form, setForm] = useState({
    patientId: 0,
    notes: "",
    tax: 0,
    discount: 0,
    lines: [{ description: "OPD consultation", quantity: 1, unitPrice: 800 }],
  });

  async function load() {
    const [inv, pat] = await Promise.all([api.get<Invoice[]>("/billing/invoices"), api.get<Patient[]>("/patients")]);
    setInvoices(inv.data);
    setPatients(pat.data);
    setForm((current) => ({ ...current, patientId: current.patientId || pat.data[0]?.id || 0 }));
  }

  useEffect(() => {
    void load();
  }, []);

  async function create(e: FormEvent) {
    e.preventDefault();
    setError("");
    try {
      await api.post("/billing/invoices", form);
      setForm((current) => ({ ...current, notes: "", tax: 0, discount: 0, lines: [{ description: "OPD consultation", quantity: 1, unitPrice: 800 }] }));
      await load();
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setError(axiosErr.response?.data?.message ?? "Could not create invoice.");
    }
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="font-display text-2xl">Billing & Invoicing</h2>
        <p className="text-sm text-slate-500">Create invoices, track balances, and hand them to Payments & POS for collection or refunds.</p>
      </div>

      {canEdit && (
        <form onSubmit={create} className="card space-y-4 p-5">
          <h3 className="font-display text-xl">New invoice</h3>
          {error && <p className="text-sm text-rose-600">{error}</p>}
          <div className="grid gap-3 md:grid-cols-3">
            <div>
              <label>Patient</label>
              <select value={form.patientId} onChange={(e) => setForm({ ...form, patientId: Number(e.target.value) })}>
                {patients.map((patient) => (
                  <option key={patient.id} value={patient.id}>
                    {patient.uhid} · {patient.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label>Tax (₹)</label>
              <input type="number" value={form.tax} onChange={(e) => setForm({ ...form, tax: Number(e.target.value) })} />
            </div>
            <div>
              <label>Discount (₹)</label>
              <input type="number" value={form.discount} onChange={(e) => setForm({ ...form, discount: Number(e.target.value) })} />
            </div>
          </div>
          {form.lines.map((line, index) => (
            <div key={index} className="grid gap-3 md:grid-cols-4">
              <div className="md:col-span-2">
                <label>Particulars</label>
                <input
                  value={line.description}
                  onChange={(e) => {
                    const lines = [...form.lines];
                    lines[index] = { ...line, description: e.target.value };
                    setForm({ ...form, lines });
                  }}
                  required
                />
              </div>
              <div>
                <label>Qty</label>
                <input
                  type="number"
                  value={line.quantity}
                  onChange={(e) => {
                    const lines = [...form.lines];
                    lines[index] = { ...line, quantity: Number(e.target.value) };
                    setForm({ ...form, lines });
                  }}
                />
              </div>
              <div>
                <label>Rate (₹)</label>
                <input
                  type="number"
                  value={line.unitPrice}
                  onChange={(e) => {
                    const lines = [...form.lines];
                    lines[index] = { ...line, unitPrice: Number(e.target.value) };
                    setForm({ ...form, lines });
                  }}
                />
              </div>
            </div>
          ))}
          <div className="flex flex-wrap gap-2">
            <button
              type="button"
              className="btn-ghost"
              onClick={() => setForm({ ...form, lines: [...form.lines, { description: "", quantity: 1, unitPrice: 0 }] })}
            >
              Add line
            </button>
            <button className="btn-primary">Create invoice</button>
          </div>
        </form>
      )}

      <section className="card overflow-hidden">
        <div className="border-b border-slate-100 px-5 py-4">
          <h3 className="font-display text-xl">Invoices</h3>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-500">
              <tr>
                <th className="px-5 py-3 font-medium">Invoice</th>
                <th className="px-5 py-3 font-medium">Patient</th>
                <th className="px-5 py-3 font-medium">Status</th>
                <th className="px-5 py-3 font-medium">Total</th>
                <th className="px-5 py-3 font-medium">Paid</th>
                <th className="px-5 py-3 font-medium">Balance</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {invoices.map((invoice) => (
                <tr key={invoice.id} className="border-t border-slate-100">
                  <td className="px-5 py-3 font-medium">
                    {invoice.number}
                    <div className="text-xs text-slate-500">{invoice.lines.map((line) => line.description).join(", ")}</div>
                  </td>
                  <td className="px-5 py-3">
                    {invoice.patientName}
                    <div className="text-xs text-slate-500">{invoice.patientUhid}</div>
                  </td>
                  <td className="px-5 py-3">{invoice.status}</td>
                  <td className="px-5 py-3">{money(invoice.total)}</td>
                  <td className="px-5 py-3">{money(invoice.paidAmount)}</td>
                  <td className="px-5 py-3 font-semibold">{money(invoice.balance)}</td>
                  <td className="px-5 py-3 text-right">
                    {canEdit && invoice.status !== "Cancelled" && invoice.paidAmount === 0 && (
                      <button className="text-rose-600 hover:underline" onClick={() => void api.post(`/billing/invoices/${invoice.id}/cancel`).then(() => load())}>
                        Cancel
                      </button>
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
