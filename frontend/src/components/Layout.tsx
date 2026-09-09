import { useEffect, useMemo, useState } from "react";
import { NavLink, Outlet, useLocation, useNavigate } from "react-router-dom";
import {
  Activity,
  Bell,
  CalendarCheck,
  LayoutDashboard,
  LogOut,
  Menu,
  PhoneCall,
  Search,
  Settings,
  UserRound,
  Users,
  LifeBuoy,
  X,
} from "lucide-react";
import { useAuth } from "../context/AuthContext";
import { api } from "../api/client";

const titles: Record<string, { title: string; subtitle: string }> = {
  "/": { title: "Overview", subtitle: "Clinic activity at a glance" },
  "/calls": { title: "Call Log", subtitle: "Who called, contact number, booked doctor, and call summary" },
  "/appointments": { title: "Appointments", subtitle: "Manage patient bookings and schedule" },
  "/patients": { title: "Patients", subtitle: "Search records, visits, and call history" },
  "/support": { title: "Support Tickets", subtitle: "Patient support requests — reply to help them" },
  "/notifications": { title: "Notifications", subtitle: "Bookings, callbacks, and live call alerts" },
  "/settings": { title: "Settings", subtitle: "Doctors, voice agent, and clinic setup" },
  "/profile": { title: "My Appointments", subtitle: "Upcoming visits and history" },
};

const clinical = [
  { to: "/", label: "Overview", icon: LayoutDashboard, roles: ["Admin", "Doctor"], end: true },
  { to: "/calls", label: "Call Log", icon: PhoneCall, roles: ["Admin", "Doctor"] },
  { to: "/appointments", label: "Appointments", icon: CalendarCheck, roles: ["Admin", "Doctor", "Patient"] },
  { to: "/patients", label: "Patients", icon: Users, roles: ["Admin", "Doctor"] },
  { to: "/support", label: "Support", icon: LifeBuoy, roles: ["Admin", "Doctor", "Patient"] },
];

const account = [
  { to: "/notifications", label: "Notifications", icon: Bell, roles: ["Admin", "Doctor"] },
  { to: "/profile", label: "My details", icon: UserRound, roles: ["Patient"] },
  { to: "/settings", label: "Settings", icon: Settings, roles: ["Admin"] },
];

export function Layout() {
  const { user, logout } = useAuth();
  const navigate = useNavigate();
  const location = useLocation();
  const [open, setOpen] = useState(false);
  const [unread, setUnread] = useState(0);
  const heading = titles[location.pathname] ?? { title: "Anuj Clinic", subtitle: "AI Voice Portal" };

  useEffect(() => {
    if (user?.role === "Patient") return;
    void api
      .get("/dashboard", { params: { page: 1, pageSize: 8 } })
      .then((res) => setUnread(res.data.callbackQueued ?? 0))
      .catch(() => undefined);
  }, [user?.role, location.pathname]);

  const clinicalLinks = useMemo(
    () => clinical.filter((link) => user && link.roles.includes(user.role)),
    [user],
  );
  const accountLinks = useMemo(
    () => account.filter((link) => user && link.roles.includes(user.role)),
    [user],
  );

  function NavGroup({ label, items }: { label: string; items: typeof clinical }) {
    if (items.length === 0) return null;
    return (
      <div className="mb-6">
        <p className="px-3 pb-2 text-[11px] font-semibold uppercase tracking-[0.18em] text-teal-200/70">{label}</p>
        <div className="space-y-1">
          {items.map((link) => (
            <NavLink
              key={link.to}
              to={link.to}
              end={link.end}
              onClick={() => setOpen(false)}
              className={({ isActive }) =>
                `flex items-center gap-3 rounded-xl px-3 py-2.5 text-sm font-medium transition ${
                  isActive ? "bg-white/15 text-white" : "text-teal-50/80 hover:bg-white/10 hover:text-white"
                }`
              }
            >
              <link.icon className="h-4 w-4" />
              {link.label}
              {link.to === "/notifications" && unread > 0 && (
                <span className="ml-auto rounded-full bg-teal-300 px-2 py-0.5 text-[10px] font-bold text-teal-950">{unread}</span>
              )}
            </NavLink>
          ))}
        </div>
      </div>
    );
  }

  function SidebarBody() {
    return (
      <>
        <div className="flex items-center gap-3 px-5 py-6">
          <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-teal-400 text-teal-950">
            <Activity className="h-5 w-5" />
          </div>
          <div>
            <p className="font-display text-lg text-white">Anuj Clinic</p>
            <p className="text-xs text-teal-100/70">AI Voice Portal</p>
          </div>
        </div>
        {user && (
          <div className="mx-4 mb-5 rounded-2xl bg-white/10 px-4 py-3">
            <p className="text-sm font-semibold text-white">{user.fullName}</p>
            <p className="text-xs text-teal-100/70">{user.role}</p>
          </div>
        )}
        <nav className="flex-1 overflow-y-auto px-3">
          <NavGroup label="Clinical" items={clinicalLinks} />
          <NavGroup label="Account" items={accountLinks} />
        </nav>
        <div className="border-t border-white/10 px-5 py-4">
          <button
            className="flex items-center gap-2 text-sm text-teal-100/80 hover:text-white"
            onClick={() => {
              logout();
              navigate("/login");
            }}
          >
            <LogOut className="h-4 w-4" />
            Sign out
          </button>
        </div>
      </>
    );
  }

  if (user?.role === "Patient") {
    return (
      <div className="min-h-screen bg-slate-50">
        <header className="border-b border-slate-200 bg-white">
          <div className="mx-auto flex max-w-5xl items-center justify-between gap-4 px-4 py-4">
            <div className="flex items-center gap-2">
              <div className="flex h-9 w-9 items-center justify-center rounded-xl bg-teal-700 text-white">
                <Activity className="h-4 w-4" />
              </div>
              <div>
                <p className="font-display text-lg text-slate-900">Anuj Clinic</p>
                <p className="text-[11px] uppercase tracking-[0.16em] text-slate-400">Patient Portal</p>
              </div>
            </div>
            <nav className="flex flex-wrap items-center justify-end gap-1 text-sm font-semibold">
              <NavLink
                to="/appointments"
                className={({ isActive }) =>
                  `rounded-lg px-3 py-2 ${isActive ? "text-teal-700 underline decoration-2 underline-offset-8" : "text-slate-600 hover:text-teal-700"}`
                }
              >
                My Appointments
              </NavLink>
              <NavLink
                to="/support"
                className={({ isActive }) =>
                  `rounded-lg px-3 py-2 ${isActive ? "text-teal-700 underline decoration-2 underline-offset-8" : "text-slate-600 hover:text-teal-700"}`
                }
              >
                Support
              </NavLink>
              <NavLink
                to="/profile"
                className={({ isActive }) =>
                  `rounded-lg px-3 py-2 ${isActive ? "text-teal-700 underline decoration-2 underline-offset-8" : "text-slate-600 hover:text-teal-700"}`
                }
              >
                My details
              </NavLink>
              <button
                className="rounded-lg px-3 py-2 text-slate-600 hover:text-rose-700"
                onClick={() => {
                  logout();
                  navigate("/login");
                }}
              >
                Sign Out
              </button>
            </nav>
          </div>
        </header>
        <main className="mx-auto max-w-5xl px-4 py-8">
          <Outlet />
        </main>
      </div>
    );
  }

  return (
    <div className="min-h-screen bg-slate-100 lg:grid lg:grid-cols-[260px_1fr]">
      <aside className="hidden min-h-screen flex-col bg-teal-950 text-white lg:flex">{SidebarBody()}</aside>

      {open && (
        <div className="fixed inset-0 z-40 lg:hidden">
          <button className="absolute inset-0 bg-slate-950/50" onClick={() => setOpen(false)} aria-label="Close menu" />
          <aside className="relative flex h-full w-72 flex-col bg-teal-950 text-white">
            <button className="absolute right-4 top-4" onClick={() => setOpen(false)} aria-label="Close navigation">
              <X className="h-5 w-5" />
            </button>
            <SidebarBody />
          </aside>
        </div>
      )}

      <div className="min-h-screen">
        <header className="flex items-center justify-between gap-4 border-b border-slate-200 bg-white px-4 py-4 sm:px-6">
          <div className="flex items-center gap-3">
            <button className="rounded-xl border border-slate-200 p-2 lg:hidden" onClick={() => setOpen(true)} aria-label="Open navigation">
              <Menu className="h-5 w-5" />
            </button>
            <div>
              <h1 className="font-display text-xl text-slate-900 sm:text-2xl">{heading.title}</h1>
              <p className="text-sm text-slate-500">{heading.subtitle}</p>
            </div>
          </div>
          <div className="flex items-center gap-3">
            <div className="hidden items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-500 md:flex">
              <Search className="h-4 w-4" />
              Search...
            </div>
            <button className="relative rounded-full border border-slate-200 p-2" onClick={() => navigate("/notifications")}>
              <Bell className="h-4 w-4 text-slate-600" />
              {unread > 0 && <span className="absolute -right-0.5 -top-0.5 h-2.5 w-2.5 rounded-full bg-teal-600" />}
            </button>
            <div className="rounded-full bg-teal-50 px-3 py-1 text-xs font-semibold text-teal-800">{user?.email}</div>
          </div>
        </header>
        <main className="p-4 sm:p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
