import { useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { api, type AuthUser } from "../api/client";
import { useAuth } from "../context/AuthContext";

export function RegisterPage() {
  const { acceptAuth } = useAuth();
  const navigate = useNavigate();
  const [fullName, setFullName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [age, setAge] = useState(30);
  const [contact, setContact] = useState("");
  const [address, setAddress] = useState("");
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(false);

  async function submit(e: FormEvent) {
    e.preventDefault();
    setError("");
    setLoading(true);
    try {
      const { data } = await api.post<AuthUser>("/auth/register", {
        fullName,
        email,
        password,
        age,
        contact,
        address,
      });
      acceptAuth(data);
      navigate("/appointments");
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setError(axiosErr.response?.data?.message ?? "Registration failed.");
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="flex min-h-screen items-center justify-center p-6">
      <form onSubmit={submit} className="card w-full max-w-md space-y-4 p-8">
        <div>
          <h1 className="font-display text-3xl">Create patient account</h1>
          <p className="mt-1 text-sm text-slate-500">Book appointments and keep your details up to date.</p>
        </div>
        {error && <div className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</div>}
        <div>
          <label>Full name</label>
          <input value={fullName} onChange={(e) => setFullName(e.target.value)} required />
        </div>
        <div>
          <label>Email</label>
          <input type="email" value={email} onChange={(e) => setEmail(e.target.value)} required />
        </div>
        <div>
          <label>Password</label>
          <input type="password" value={password} onChange={(e) => setPassword(e.target.value)} required />
        </div>
        <div className="grid grid-cols-2 gap-3">
          <div>
            <label>Age</label>
            <input type="number" value={age} onChange={(e) => setAge(Number(e.target.value))} />
          </div>
          <div>
            <label>Contact</label>
            <input value={contact} onChange={(e) => setContact(e.target.value)} required />
          </div>
        </div>
        <div>
          <label>Address</label>
          <input value={address} onChange={(e) => setAddress(e.target.value)} />
        </div>
        <button className="btn-primary w-full" disabled={loading}>
          {loading ? "Creating account..." : "Register"}
        </button>
        <Link to="/login" className="block text-center text-sm text-teal-700">
          Already have an account? Sign in
        </Link>
      </form>
    </div>
  );
}
