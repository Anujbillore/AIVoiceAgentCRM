import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { Activity, Eye, EyeOff } from "lucide-react";
import { useAuth } from "../context/AuthContext";
import { isValidEmail } from "../lib/validate";

const demos = [
  { label: "Admin", email: "admin@clinic.com", password: "Admin@123", hint: "Full clinic dashboard" },
  { label: "Doctor", email: "mehta@clinic.com", password: "Doctor@123", hint: "Your bookings & call logs" },
  { label: "Patient", email: "patient@clinic.com", password: "Patient@123", hint: "Reschedule or cancel" },
];

export function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();
  const [email, setEmail] = useState(() => localStorage.getItem("clinic.lastEmail") ?? "");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError("");
    if (!email.trim() || !isValidEmail(email) || !email.includes("@")) {
      setError("Enter a valid email address.");
      return;
    }
    if (!password.trim()) {
      setError("Enter your password.");
      return;
    }
    setLoading(true);
    try {
      localStorage.setItem("clinic.lastEmail", email.trim());
      const signedIn = await login(email.trim(), password);
      navigate(signedIn.role === "Patient" ? "/appointments" : "/");
    } catch (err: unknown) {
      const status = (err as { response?: { status?: number; data?: { message?: string } } })?.response?.status;
      const message = (err as { response?: { data?: { message?: string } } })?.response?.data?.message;
      if (!status) {
        setError("Cannot reach the clinic server. Refresh this page and try again.");
      } else if (status === 401) {
        setError(message || "That email or password is not correct. Try again, or use Forgot password.");
      } else {
        setError(message || "Sign-in failed. Try again.");
      }
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
          <h2 className="mt-4 font-display text-5xl leading-tight">Sign in to manage calls, bookings, and patient visits.</h2>
          <p className="mt-5 text-lg text-slate-300">
            Staff see the full clinic. Doctors see their own appointments. Patients can reschedule or cancel online.
          </p>
        </div>
        <p className="relative text-sm text-slate-400">Same sign-in for admin, doctor, and patient accounts.</p>
      </div>
      <div className="flex items-center justify-center p-6 sm:p-8">
        <form onSubmit={submit} className="w-full max-w-md space-y-5" noValidate>
          <div className="flex items-center gap-3 lg:hidden">
            <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-teal-700 text-white">
              <Activity className="h-5 w-5" />
            </div>
            <span className="font-display text-xl">Anuj Clinic</span>
          </div>
          <div>
            <h1 className="font-display text-3xl">Welcome back</h1>
            <p className="mt-1 text-slate-500">Enter your email and password to continue.</p>
          </div>
          {error && (
            <div className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700" role="alert">
              {error}
            </div>
          )}
          <div>
            <label htmlFor="email">Email</label>
            <input
              id="email"
              name="email"
              type="email"
              autoComplete="username"
              placeholder="you@clinic.com"
              value={email}
              onChange={(e) => setEmail(e.target.value)}
              required
              aria-invalid={Boolean(error)}
            />
          </div>
          <div>
            <label htmlFor="password">Password</label>
            <div className="relative">
              <input
                id="password"
                name="password"
                type={showPassword ? "text" : "password"}
                autoComplete="current-password"
                placeholder="Enter your password"
                className="pr-12"
                value={password}
                onChange={(e) => setPassword(e.target.value)}
                required
                aria-invalid={Boolean(error)}
              />
              <button
                type="button"
                className="absolute right-3 top-1/2 -translate-y-1/2 text-slate-400 hover:text-slate-700"
                onClick={() => setShowPassword((open) => !open)}
                aria-label={showPassword ? "Hide password" : "Show password"}
              >
                {showPassword ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
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
          <div className="rounded-2xl bg-slate-50 p-4">
            <p className="text-xs font-semibold uppercase tracking-[0.14em] text-slate-500">Try a demo account</p>
            <div className="mt-3 grid gap-2 sm:grid-cols-3">
              {demos.map((demo) => (
                <button
                  key={demo.email}
                  type="button"
                  className="rounded-xl border border-slate-200 bg-white px-3 py-2 text-left hover:border-teal-300 hover:bg-teal-50"
                  onClick={() => {
                    setEmail(demo.email);
                    setPassword(demo.password);
                    setError("");
                  }}
                >
                  <p className="text-sm font-semibold text-slate-800">{demo.label}</p>
                  <p className="text-[11px] text-slate-500">{demo.hint}</p>
                </button>
              ))}
            </div>
          </div>
        </form>
      </div>
    </div>
  );
}
