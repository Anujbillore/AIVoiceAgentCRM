import { useEffect, useState, type FormEvent } from "react";
import { api, type Invoice, type Patient } from "../api/client";
import { useAuth } from "../context/AuthContext";
import { PaymentsPage } from "./Payments";
import { isPositiveAmount } from "../lib/validate";
import { StatusBadge } from "../components/StatusBadge";
import { Pagination } from "../components/Pagination";
import { pagerProps, usePaged } from "../lib/pager";

function money(value: number) {
  return `₹${value.toLocaleString("en-IN", { maximumFractionDigits: 2 })}`;
}

export function BillingPage() {
  const { user } = useAuth();
  const canEdit = user?.role === "Admin";
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [patients, setPatients] = useState<Patient[]>([]);
  const [error, setError] = useState("");
  const [tab, setTab] = useState<"bills" | "payments">("bills");
  const [patientQuery, setPatientQuery] = useState("");
  const [form, setForm] = useState({
    patientId: 0,
    notes: "",
    tax: 0,
    discount: 0,
    lines: [{ description: "OPD consultation", quantity: 1, unitPrice: 800 }],
  });
  const pagedInvoices = usePaged(invoices, 8);

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
    if (!form.patientId) {
      setError("Select a patient.");
      return;
    }
    if (form.lines.some((line) => !line.description.trim() || !isPositiveAmount(line.unitPrice) || !isPositiveAmount(line.quantity))) {
      setError("Each bill item needs a description, quantity, and a price greater than 0.");
      return;
    }
    if (form.tax < 0 || form.discount < 0) {
      setError("Tax and discount cannot be negative.");
      return;
    }
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
        <h2 className="font-display text-2xl">Billing</h2>
        <p className="text-sm text-slate-500">Create bills and record payments. History stays with the patient.</p>
      </div>
      <div className="grid grid-cols-2 gap-2 rounded-2xl bg-slate-100 p-1 sm:w-96">
        <button type="button" className={`rounded-xl px-3 py-2 text-sm font-semibold ${tab === "bills" ? "bg-white text-teal-800 shadow" : "text-slate-500"}`} onClick={() => setTab("bills")}>
          Bills
        </button>
        <button type="button" className={`rounded-xl px-3 py-2 text-sm font-semibold ${tab === "payments" ? "bg-white text-teal-800 shadow" : "text-slate-500"}`} onClick={() => setTab("payments")}>
          Payment history
        </button>
      </div>
      {tab === "payments" ? <PaymentsPage embedded /> : (
      <>
      {canEdit && (
        <form onSubmit={create} className="card space-y-4 p-5">
          <h3 className="font-display text-xl">New bill</h3>
          {error && <p className="text-sm text-rose-600">{error}</p>}
          <div className="grid gap-3 md:grid-cols-3">
            <div>
              <label>Search patient</label>
              <input value={patientQuery} onChange={(e) => setPatientQuery(e.target.value)} placeholder="Name, phone or UHID" />
              <select className="mt-2" value={form.patientId} onChange={(e) => setForm({ ...form, patientId: Number(e.target.value) })} required>
                <option value={0}>Select a patient</option>
                {patients
                  .filter((patient) => {
                    const term = patientQuery.trim().toLowerCase();
                    return !term || patient.name.toLowerCase().includes(term) || patient.contact.toLowerCase().includes(term) || patient.uhid.toLowerCase().includes(term);
                  })
                  .map((patient) => (
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
              <input type="number" min={0} value={form.discount} onChange={(e) => setForm({ ...form, discount: Number(e.target.value) })} />
            </div>
          </div>
          <p className="text-sm text-slate-600">
            Bill date {new Date().toLocaleDateString()} · Subtotal ₹{form.lines.reduce((sum, line) => sum + Number(line.quantity || 0) * Number(line.unitPrice || 0), 0).toLocaleString("en-IN")} · Total ₹{Math.max(0, form.lines.reduce((sum, line) => sum + Number(line.quantity || 0) * Number(line.unitPrice || 0), 0) + Number(form.tax || 0) - Number(form.discount || 0)).toLocaleString("en-IN")}
          </p>
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
            <button className="btn-primary">Create bill</button>
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
              {pagedInvoices.items.map((invoice) => (
                <tr key={invoice.id} className="border-t border-slate-100">
                  <td className="px-5 py-3 font-medium">
                    {invoice.number}
                    <div className="text-xs text-slate-500">{invoice.lines.map((line) => line.description).join(", ")}</div>
                  </td>
                  <td className="px-5 py-3">
                    {invoice.patientName}
                    <div className="text-xs text-slate-500">{invoice.patientUhid}</div>
                  </td>
                  <td className="px-5 py-3"><StatusBadge status={invoice.status} /></td>
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
        <Pagination {...pagerProps(pagedInvoices)} />
      </section>
      </>
      )}
    </div>
  );
}
