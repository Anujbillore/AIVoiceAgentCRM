import { useState } from "react";
import { NavLink, Outlet, useNavigate } from "react-router-dom";
import {
  Activity,
  CalendarCheck,
  CreditCard,
  LayoutDashboard,
  LogOut,
  Menu,
  PhoneCall,
  Receipt,
  Settings,
  Shield,
  UserRound,
  Users,
  X,
} from "lucide-react";
import { useAuth } from "../context/AuthContext";

const links = [
  { to: "/", label: "Dashboard", icon: LayoutDashboard, roles: ["Admin", "Doctor", "Patient"] },
  { to: "/patients", label: "Patient Management", icon: Users, roles: ["Admin", "Doctor"] },
  { to: "/profile", label: "My details", icon: UserRound, roles: ["Patient"] },
  { to: "/appointments", label: "Appointment Scheduling", icon: CalendarCheck, roles: ["Admin", "Doctor", "Patient"] },
  { to: "/billing", label: "Billing & Invoicing", icon: Receipt, roles: ["Admin", "Doctor", "Patient"] },
  { to: "/insurance", label: "Insurance / Claims", icon: Shield, roles: ["Admin", "Doctor", "Patient"] },
  { to: "/payments", label: "Payments & POS", icon: CreditCard, roles: ["Admin", "Doctor"] },
  { to: "/voice", label: "Voice Agent", icon: PhoneCall, roles: ["Admin", "Doctor"] },
  { to: "/settings", label: "Settings", icon: Settings, roles: ["Admin"] },
];

export function Layout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const [open, setOpen] = useState(false);
  const visible = links.filter((link) => user && link.roles.includes(user.role));

  function NavItems() {
    return (
      <>
        {visible.map((link) => (
          <NavLink
            key={link.to}
            to={link.to}
            end={link.to === "/"}
            onClick={() => setOpen(false)}
            className={({ isActive }) =>
              `flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition ${
                isActive ? "bg-teal-500/15 text-teal-200" : "text-slate-300 hover:bg-white/5"
              }`
            }
          >
            <link.icon className="h-4 w-4" />
            {link.label}
          </NavLink>
        ))}
      </>
    );
  }

  return (
    <div className="min-h-screen bg-slate-50 lg:grid lg:grid-cols-[280px_1fr]">
      <aside className="hidden min-h-screen flex-col bg-slate-950 text-slate-200 lg:flex">
        <div className="flex items-center gap-3 px-6 py-6">
          <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-teal-500 text-slate-950">
            <Activity className="h-5 w-5" />
          </div>
          <div>
            <p className="font-display text-lg text-white">Anuj Clinic</p>
            <p className="text-xs text-slate-400">AI Voice Agent Portal</p>
          </div>
        </div>
        <nav className="flex-1 space-y-1 px-3 pb-8">
          <NavItems />
        </nav>
        <div className="mt-auto border-t border-white/10 px-6 py-5">
          <p className="text-sm font-semibold text-white">{user?.fullName}</p>
          <p className="text-xs text-slate-400">{user?.role}</p>
          <button
            className="mt-3 flex items-center gap-2 text-sm text-slate-400 hover:text-white"
            onClick={() => {
              logout();
              navigate("/login");
            }}
          >
            <LogOut className="h-4 w-4" />
            Sign out
          </button>
        </div>
      </aside>

      {open && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <button className="absolute inset-0 bg-slate-950/50" onClick={() => setOpen(false)} aria-label="Close menu" />
          <aside className="relative flex h-full w-72 flex-col bg-slate-950 text-slate-200">
            <div className="flex items-center justify-between px-5 py-5">
              <p className="font-display text-lg text-white">Anuj Clinic</p>
              <button onClick={() => setOpen(false)} aria-label="Close navigation">
                <X className="h-5 w-5" />
              </button>
            </div>
            <nav className="flex-1 space-y-1 px-3">
              <NavItems />
            </nav>
          </aside>
        </div>
      )}

      <div className="min-h-screen">
        <header className="flex items-center justify-between border-b border-slate-200 bg-white px-4 py-4 sm:px-6">
          <div className="flex items-center gap-3">
            <button className="rounded-xl border border-slate-200 p-2 lg:hidden" onClick={() => setOpen(true)} aria-label="Open navigation">
              <Menu className="h-5 w-5" />
            </button>
            <div>
              <p className="text-xs uppercase tracking-[0.2em] text-teal-700">Live clinic ops</p>
              <h1 className="font-display text-xl text-slate-900 sm:text-2xl">AI Voice Agent Web Portal</h1>
            </div>
          </div>
          <div className="rounded-full bg-teal-50 px-3 py-1 text-xs font-semibold text-teal-800">{user?.email}</div>
        </header>
        <main className="p-4 sm:p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
