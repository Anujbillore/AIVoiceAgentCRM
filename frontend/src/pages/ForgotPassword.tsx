import { useState, type FormEvent } from "react";
import { Link } from "react-router-dom";
import { api } from "../api/client";

export function ForgotPasswordPage() {
  const [email, setEmail] = useState("");
  const [message, setMessage] = useState("");
  const [loading, setLoading] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setLoading(true);
    setMessage("");
    try {
      const { data } = await api.post("/auth/forgot-password", { email });
      setMessage(data.message);
    } catch {
      setMessage("Could not send reset email.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center p-6">
      <form onSubmit={submit} className="card w-full max-w-md space-y-4 p-8">
        <h1 className="font-display text-3xl">Reset password</h1>
        <p className="text-sm text-slate-500">We’ll email a reset link if the account exists. Without SMTP, the link is written to the API logs.</p>
        {message && <div className="rounded-xl bg-teal-50 px-3 py-2 text-sm text-teal-800">{message}</div>}
        <div>
          <label htmlFor="email">Email</label>
          <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </div>
        <button className="btn-primary w-full" disabled={loading}>
          {loading ? "Sending..." : "Send reset link"}
        </button>
        <Link to="/login" className="block text-center text-sm text-teal-700">
          Back to sign in
        </Link>
      </form>
    </div>
  );
}
