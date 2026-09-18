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
  Settings,
  UserRound,
  Users,
  LifeBuoy,
  X,
} from "lucide-react";
import { useAuth } from "../context/AuthContext";
import { api } from "../api/client";
import { GlobalSearch } from "./GlobalSearch";

const titles: Record<string, { title: string; subtitle: string }> = {
  "/": { title: "Overview", subtitle: "Upcoming appointments and call activity" },
  "/calls": { title: "Call Log", subtitle: "Who called, contact number, booked doctor, and call summary" },
  "/appointments": { title: "Appointments", subtitle: "Manage patient bookings and schedule" },
  "/patients": { title: "Patients", subtitle: "Search records, visits, and call history" },
  "/support": { title: "Support Tickets", subtitle: "Patient support requests — reply to help them" },
  "/notifications": { title: "Notifications", subtitle: "Bookings, callbacks, and live call alerts" },
  "/settings": { title: "Settings", subtitle: "Doctors, voice agent, and clinic setup" },
  "/profile": { title: "My details", subtitle: "Contact info and visit history" },
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
  const heading = { ...(titles[location.pathname] ?? { title: "Anuj Clinic", subtitle: "AI Voice Portal" }) };
  if (user?.role === "Doctor") {
    if (location.pathname === "/") {
      heading.subtitle = "Your upcoming appointments and your booked call logs";
    }
    if (location.pathname === "/calls") {
      heading.subtitle = "Only calls for appointments booked with you";
    }
    if (location.pathname === "/appointments") {
      heading.subtitle = "Patients who booked with you";
    }
  }

  useEffect(() => {
    if (user?.role === "Patient") return;
    async function loadUnread() {
      try {
        const { data } = await api.get<{ count: number }>("/notifications/unread-count");
        setUnread(data.count ?? 0);
      } catch {
        setUnread(0);
      }
    }
    void loadUnread();
  }, [user?.role, location.pathname]);

  useEffect(() => {
    setOpen(false);
  }, [location.pathname]);

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
            <p className="truncate text-sm font-semibold text-white">{user.fullName}</p>
            <p className="text-xs text-teal-100/70">{user.role}</p>
          </div>
        )}
        <nav className="flex-1 overflow-y-auto px-3 pb-4">
          <NavGroup label="Clinical" items={clinicalLinks} />
          <NavGroup label="Account" items={accountLinks} />
        </nav>
      </>
    );
  }

  function signOut() {
    logout();
    navigate("/login");
  }

  if (user?.role === "Patient") {
    return (
      <div className="min-h-screen bg-slate-50">
        <header className="sticky top-0 z-30 border-b border-slate-200 bg-white/95 backdrop-blur">
          <div className="mx-auto flex max-w-5xl flex-col gap-3 px-4 py-3 sm:py-4">
            <div className="flex items-center justify-between gap-3">
              <div className="flex min-w-0 items-center gap-2">
                <div className="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-teal-700 text-white">
                  <Activity className="h-4 w-4" />
                </div>
                <div className="min-w-0">
                  <p className="font-display text-lg text-slate-900">Anuj Clinic</p>
                  <p className="text-[11px] uppercase tracking-[0.16em] text-slate-400">Patient Portal</p>
                </div>
              </div>
              <button
                className="inline-flex shrink-0 items-center gap-1.5 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-sm font-semibold text-rose-700 hover:bg-rose-100"
                onClick={signOut}
              >
                <LogOut className="h-4 w-4" />
                <span className="hidden xs:inline sm:inline">Sign out</span>
              </button>
            </div>
            <nav className="-mx-4 flex gap-1 overflow-x-auto px-4 pb-1 text-sm font-semibold">
              <NavLink
                to="/appointments"
                className={({ isActive }) =>
                  `whitespace-nowrap rounded-lg px-3 py-2 ${isActive ? "bg-teal-50 text-teal-700" : "text-slate-600 hover:text-teal-700"}`
                }
              >
                My Appointments
              </NavLink>
              <NavLink
                to="/support"
                className={({ isActive }) =>
                  `whitespace-nowrap rounded-lg px-3 py-2 ${isActive ? "bg-teal-50 text-teal-700" : "text-slate-600 hover:text-teal-700"}`
                }
              >
                Support
              </NavLink>
              <NavLink
                to="/profile"
                className={({ isActive }) =>
                  `whitespace-nowrap rounded-lg px-3 py-2 ${isActive ? "bg-teal-50 text-teal-700" : "text-slate-600 hover:text-teal-700"}`
                }
              >
                My details
              </NavLink>
            </nav>
          </div>
        </header>
        <main className="mx-auto max-w-5xl px-4 py-5 sm:py-8">
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
          <aside className="relative flex h-full w-[min(100vw-3rem,18rem)] flex-col bg-teal-950 text-white shadow-2xl">
            <button className="absolute right-4 top-4" onClick={() => setOpen(false)} aria-label="Close navigation">
              <X className="h-5 w-5" />
            </button>
            <SidebarBody />
          </aside>
        </div>
      )}

      <div className="min-h-screen min-w-0">
        <header className="sticky top-0 z-30 border-b border-slate-200 bg-white/95 backdrop-blur">
          <div className="flex flex-col gap-3 px-4 py-3 sm:px-6 sm:py-4">
            <div className="flex items-start justify-between gap-3">
              <div className="flex min-w-0 items-start gap-3">
                <button
                  className="mt-0.5 shrink-0 rounded-xl border border-slate-200 p-2 lg:hidden"
                  onClick={() => setOpen(true)}
                  aria-label="Open navigation"
                >
                  <Menu className="h-5 w-5" />
                </button>
                <div className="min-w-0">
                  <h1 className="font-display text-xl text-slate-900 sm:text-2xl">{heading.title}</h1>
                  <p className="hidden text-sm text-slate-500 sm:block">{heading.subtitle}</p>
                </div>
              </div>
              <div className="flex shrink-0 items-center gap-2">
                <div className="hidden md:block">
                  <GlobalSearch />
                </div>
                <div className="md:hidden">
                  <GlobalSearch compact />
                </div>
                <button
                  className="relative rounded-full border border-slate-200 p-2"
                  onClick={() => navigate("/notifications")}
                  aria-label="Notifications"
                >
                  <Bell className="h-4 w-4 text-slate-600" />
                  {unread > 0 && <span className="absolute -right-0.5 -top-0.5 h-2.5 w-2.5 rounded-full bg-teal-600" />}
                </button>
                <button
                  className="inline-flex items-center gap-1.5 rounded-xl border border-rose-200 bg-rose-50 px-3 py-2 text-sm font-semibold text-rose-700 hover:bg-rose-100"
                  onClick={signOut}
                >
                  <LogOut className="h-4 w-4" />
                  <span className="hidden sm:inline">Sign out</span>
                </button>
              </div>
            </div>
          </div>
        </header>
        <main className="min-w-0 p-3 sm:p-6">
          <Outlet />
        </main>
      </div>
    </div>
  );
}
