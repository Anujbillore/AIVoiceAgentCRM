import { useState, type FormEvent } from "react";
import { Link, useSearchParams } from "react-router-dom";
import { api } from "../api/client";

export function ResetPasswordPage() {
  const [params] = useSearchParams();
  const [email, setEmail] = useState(params.get("email") ?? "");
  const [token, setToken] = useState(params.get("token") ?? "");
  const [password, setPassword] = useState("");
  const [message, setMessage] = useState("");
  const [error, setError] = useState("");

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError("");
    setMessage("");
    try {
      const { data } = await api.post("/auth/reset-password", { email, token, newPassword: password });
      setMessage(data.message);
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setError(axiosErr.response?.data?.message ?? "Reset failed.");
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center p-6">
      <form onSubmit={submit} className="card w-full max-w-md space-y-4 p-8">
        <h1 className="font-display text-3xl">Choose a new password</h1>
        {message && <div className="rounded-xl bg-teal-50 px-3 py-2 text-sm text-teal-800">{message}</div>}
        {error && <div className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</div>}
        <div>
          <label>Email</label>
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </div>
        <div>
          <label>New password</label>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </div>
        <input type="hidden" value={token} onChange={(e) => setToken(e.target.value)} />
        <button className="btn-primary w-full">Update password</button>
        <Link to="/login" className="block text-center text-sm text-teal-700">
          Back to sign in
        </Link>
      </form>
    </div>
  );
}
