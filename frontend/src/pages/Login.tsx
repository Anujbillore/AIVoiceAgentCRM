import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Activity } from "lucide-react";
import { useAuth } from "../context/AuthContext";

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState("admin@clinic.com");
  const [password, setPassword] = useState("Admin@123");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError("");
    setLoading(true);
    try {
      const signedIn = await login(email, password);
      navigate(signedIn.role === "Patient" ? "/appointments" : signedIn.role === "Doctor" ? "/appointments" : "/");
    } catch {
      setError("Invalid email or password.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="grid min-h-screen lg:grid-cols-2">
      <div className="relative hidden overflow-hidden bg-slate-950 p-12 text-white lg:flex lg:flex-col lg:justify-between">
        <div className="absolute inset-0 bg-[radial-gradient(circle_at_top_left,rgba(20,184,166,0.35),transparent_45%),radial-gradient(circle_at_bottom_right,rgba(45,212,191,0.2),transparent_40%)]" />
        <div className="relative flex items-center gap-3">
          <div className="flex h-11 w-11 items-center justify-center rounded-2xl bg-teal-400 text-slate-950">
            <Activity className="h-5 w-5" />
          </div>
          <span className="font-display text-2xl">Anuj Clinic</span>
        </div>
        <div className="relative max-w-lg">
          <p className="text-sm uppercase tracking-[0.25em] text-teal-200">Voice-first care</p>
          <h2 className="mt-4 font-display text-5xl leading-tight">AI receptionist for appointments and doctor schedules.</h2>
          <p className="mt-5 text-lg text-slate-300">
            Incoming calls are greeted, understood, booked, summarized, and emailed — live on the dashboard.
          </p>
        </div>
        <p className="relative text-sm text-slate-400">Powered by ASP.NET Identity, SignalR, and Sarvam AI.</p>
      </div>
      <div className="flex items-center justify-center p-8">
        <form onSubmit={submit} className="w-full max-w-md space-y-5">
          <div>
            <h1 className="font-display text-3xl">Welcome back</h1>
            <p className="mt-1 text-slate-500">Sign in with your clinic role to continue.</p>
          </div>
          {error && <div className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</div>}
          <div>
            <label htmlFor="email">Email</label>
            <input id="email" type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
          </div>
          <div>
            <label htmlFor="password">Password</label>
            <input id="password" type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
          </div>
          <div className="flex items-center justify-between text-sm">
            <Link to="/forgot-password" className="text-teal-700 hover:underline">
              Forgot password?
            </Link>
            <Link to="/register" className="text-teal-700 hover:underline">
              Create patient account
            </Link>
          </div>
          <button className="btn-primary w-full" disabled={loading}>
            {loading ? "Signing in..." : "Sign in"}
          </button>
          <div className="rounded-2xl bg-slate-50 p-4 text-xs text-slate-600">
            <p className="font-semibold text-slate-800">Demo accounts</p>
            <p>Admin — admin@clinic.com / Admin@123</p>
            <p>Doctors sign in with the Gmail and password saved in Settings.</p>
            <p>Demo doctor — mehta@clinic.com / Doctor@123</p>
            <p>Patient — patient@clinic.com / Patient@123</p>
          </div>
        </form>
      </div>
    </div>
  );
}
