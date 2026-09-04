import { useEffect, useMemo, useState, type FormEvent } from "react";
import { api, type Invoice, type Patient, type Payment } from "../api/client";
import { useAuth } from "../context/AuthContext";

function money(value: number) {
  return `₹${value.toLocaleString("en-IN", { maximumFractionDigits: 2 })}`;
}

function printReceipt(payment: Payment) {
  const win = window.open("", "_blank", "width=420,height=640");
  if (!win) return;
  win.document.write(`
    <html><head><title>${payment.receiptNumber}</title>
    <style>body{font-family:ui-sans-serif,system-ui;padding:24px} h1{font-size:20px} table{width:100%;font-size:13px}</style>
    </head><body>
    <h1>Anuj Clinic receipt</h1>
    <p>${payment.receiptNumber}<br/>${new Date(payment.paidAt).toLocaleString()}</p>
    <p>Patient: ${payment.patientName}<br/>Invoice: ${payment.invoiceNumber ?? "Walk-in / POS"}</p>
    <table>
      ${payment.splits.map((split) => `<tr><td>${split.method}</td><td style="text-align:right">${money(split.amount)}</td></tr>`).join("")}
      <tr><td><strong>Total</strong></td><td style="text-align:right"><strong>${money(payment.amount)}</strong></td></tr>
    </table>
    <p>Gateway: ${payment.gateway}<br/>Ref: ${payment.reference}</p>
    <p>Status: ${payment.status}${payment.refundedAmount ? ` · Refunded ${money(payment.refundedAmount)}` : ""}</p>
    </body></html>
  `);
  win.document.close();
  win.print();
}

export function PaymentsPage() {
  const { user } = useAuth();
  const canEdit = user?.role === "Admin" || user?.role === "Doctor";
  const [payments, setPayments] = useState<Payment[]>([]);
  const [invoices, setInvoices] = useState<Invoice[]>([]);
  const [patients, setPatients] = useState<Patient[]>([]);
  const [error, setError] = useState("");
  const [refund, setRefund] = useState({ id: 0, amount: 0, reason: "" });
  const [form, setForm] = useState({
    patientId: 0,
    invoiceId: "",
    notes: "",
    splits: [{ method: "UPI", amount: 0 }],
  });

  const openInvoices = useMemo(
    () => invoices.filter((invoice) => invoice.patientId === form.patientId && invoice.balance > 0 && invoice.status !== "Cancelled"),
    [invoices, form.patientId],
  );

  async function load() {
    const [pay, inv, pat] = await Promise.all([
      api.get<Payment[]>("/payments"),
      api.get<Invoice[]>("/billing/invoices"),
      api.get<Patient[]>("/patients"),
    ]);
    setPayments(pay.data);
    setInvoices(inv.data);
    setPatients(pat.data);
    setForm((current) => ({ ...current, patientId: current.patientId || pat.data[0]?.id || 0 }));
  }

  useEffect(() => {
    void load();
  }, []);

  async function collect(e: FormEvent) {
    e.preventDefault();
    setError("");
    try {
      await api.post("/payments", {
        patientId: form.patientId,
        invoiceId: form.invoiceId ? Number(form.invoiceId) : null,
        notes: form.notes,
        splits: form.splits,
      });
      setForm((current) => ({ ...current, notes: "", invoiceId: "", splits: [{ method: "UPI", amount: 0 }] }));
      await load();
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setError(axiosErr.response?.data?.message ?? "Payment failed.");
    }
  }

  async function submitRefund() {
    if (!refund.id || !refund.amount) return;
    await api.post(`/payments/${refund.id}/refund`, { amount: refund.amount, reason: refund.reason });
    setRefund({ id: 0, amount: 0, reason: "" });
    await load();
  }

  return (
    <div className="space-y-6">
      <div>
        <h2 className="font-display text-2xl">Payments & POS</h2>
        <p className="text-sm text-slate-500">
          Cash, card, UPI, and ACH. Split a bill across methods. Card/UPI/ACH go through the demo payment gateway and return a reference.
        </p>
      </div>

      {canEdit && (
        <form onSubmit={collect} className="card space-y-4 p-5">
          <h3 className="font-display text-xl">Collect payment</h3>
          {error && <p className="text-sm text-rose-600">{error}</p>}
          <div className="grid gap-3 md:grid-cols-2">
            <div>
              <label>Patient</label>
              <select value={form.patientId} onChange={(e) => setForm({ ...form, patientId: Number(e.target.value), invoiceId: "" })}>
                {patients.map((patient) => (
                  <option key={patient.id} value={patient.id}>
                    {patient.uhid} · {patient.name}
                  </option>
                ))}
              </select>
            </div>
            <div>
              <label>Apply to invoice</label>
              <select value={form.invoiceId} onChange={(e) => setForm({ ...form, invoiceId: e.target.value })}>
                <option value="">Walk-in / unapplied</option>
                {openInvoices.map((invoice) => (
                  <option key={invoice.id} value={invoice.id}>
                    {invoice.number} · due {money(invoice.balance)}
                  </option>
                ))}
              </select>
            </div>
          </div>
          {form.splits.map((split, index) => (
            <div key={index} className="grid gap-3 md:grid-cols-2">
              <div>
                <label>Method</label>
                <select
                  value={split.method}
                  onChange={(e) => {
                    const splits = [...form.splits];
                    splits[index] = { ...split, method: e.target.value };
                    setForm({ ...form, splits });
                  }}
                >
                  <option>Cash</option>
                  <option>Card</option>
                  <option>UPI</option>
                  <option>ACH</option>
                </select>
              </div>
              <div>
                <label>Amount (₹)</label>
                <input
                  type="number"
                  value={split.amount}
                  onChange={(e) => {
                    const splits = [...form.splits];
                    splits[index] = { ...split, amount: Number(e.target.value) };
                    setForm({ ...form, splits });
                  }}
                  required
                />
              </div>
            </div>
          ))}
          <div>
            <label>Notes</label>
            <input value={form.notes} onChange={(e) => setForm({ ...form, notes: e.target.value })} placeholder="POS counter 1" />
          </div>
          <div className="flex flex-wrap gap-2">
            <button type="button" className="btn-ghost" onClick={() => setForm({ ...form, splits: [...form.splits, { method: "Cash", amount: 0 }] })}>
              Split payment
            </button>
            <button className="btn-primary">Charge {money(form.splits.reduce((sum, split) => sum + Number(split.amount || 0), 0))}</button>
          </div>
        </form>
      )}

      {refund.id > 0 && canEdit && (
        <section className="card space-y-3 p-5">
          <h3 className="font-display text-xl">Refund</h3>
          <input type="number" value={refund.amount} onChange={(e) => setRefund({ ...refund, amount: Number(e.target.value) })} />
          <input value={refund.reason} onChange={(e) => setRefund({ ...refund, reason: e.target.value })} placeholder="Reason" />
          <div className="flex gap-2">
            <button className="btn-primary" onClick={() => void submitRefund()}>
              Refund
            </button>
            <button className="btn-ghost" onClick={() => setRefund({ id: 0, amount: 0, reason: "" })}>
              Cancel
            </button>
          </div>
        </section>
      )}

      <section className="card overflow-hidden">
        <div className="border-b border-slate-100 px-5 py-4">
          <h3 className="font-display text-xl">Receipts</h3>
        </div>
        <div className="overflow-x-auto">
          <table className="min-w-full text-left text-sm">
            <thead className="bg-slate-50 text-slate-500">
              <tr>
                <th className="px-5 py-3 font-medium">Receipt</th>
                <th className="px-5 py-3 font-medium">Patient</th>
                <th className="px-5 py-3 font-medium">Methods</th>
                <th className="px-5 py-3 font-medium">Amount</th>
                <th className="px-5 py-3 font-medium">Status</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {payments.map((payment) => (
                <tr key={payment.id} className="border-t border-slate-100">
                  <td className="px-5 py-3 font-medium">
                    {payment.receiptNumber}
                    <div className="text-xs text-slate-500">{payment.reference}</div>
                  </td>
                  <td className="px-5 py-3">
                    {payment.patientName}
                    <div className="text-xs text-slate-500">{payment.invoiceNumber ?? "POS"}</div>
                  </td>
                  <td className="px-5 py-3">{payment.splits.map((split) => `${split.method} ${money(split.amount)}`).join(" + ")}</td>
                  <td className="px-5 py-3">{money(payment.amount)}</td>
                  <td className="px-5 py-3">{payment.status}</td>
                  <td className="px-5 py-3 text-right">
                    <button className="text-teal-700 hover:underline" onClick={() => printReceipt(payment)}>
                      Receipt
                    </button>
                    {canEdit && payment.status !== "Refunded" && (
                      <button
                        className="ml-3 text-rose-600 hover:underline"
                        onClick={() => setRefund({ id: payment.id, amount: payment.amount - payment.refundedAmount, reason: "" })}
                      >
                        Refund
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
