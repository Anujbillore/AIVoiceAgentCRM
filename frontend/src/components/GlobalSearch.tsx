import { useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { CalendarCheck, PhoneCall, Search, UserRound, X } from "lucide-react";
import { api, type AppointmentPage, type CallLog, type DashboardStats, type Patient } from "../api/client";
import { isoDate } from "../lib/format";
import { useAuth } from "../context/AuthContext";

interface Hit {
  id: string;
  kind: "Patient" | "Appointment" | "Call";
  title: string;
  detail: string;
  to: string;
}

export function GlobalSearch({ compact = false }: { compact?: boolean }) {
  const { user } = useAuth();
  const navigate = useNavigate();
  const [query, setQuery] = useState("");
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [hits, setHits] = useState<Hit[]>([]);
  const [expanded, setExpanded] = useState(!compact);
  const boxRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    function onDoc(e: MouseEvent) {
      if (!boxRef.current?.contains(e.target as Node)) {
        setOpen(false);
        if (compact) setExpanded(false);
      }
    }
    document.addEventListener("mousedown", onDoc);
    return () => document.removeEventListener("mousedown", onDoc);
  }, [compact]);

  useEffect(() => {
    const term = query.trim();
    if (term.length < 2) {
      setHits([]);
      return;
    }

    let cancelled = false;
    const timer = window.setTimeout(async () => {
      setBusy(true);
      try {
        const today = isoDate(new Date());
        const from = isoDate(new Date(Date.now() - 29 * 24 * 60 * 60 * 1000));
        const requests: Promise<Hit[]>[] = [
          api.get<AppointmentPage>("/appointment", { params: { page: 1, pageSize: 50 } }).then((res) =>
            res.data.items
              .filter(
                (item) =>
                  item.patientName.toLowerCase().includes(term.toLowerCase()) ||
                  item.doctorName.toLowerCase().includes(term.toLowerCase()) ||
                  item.patientContact.toLowerCase().includes(term.toLowerCase()),
              )
              .slice(0, 6)
              .map((item) => ({
                id: `appt-${item.id}`,
                kind: "Appointment" as const,
                title: item.patientName,
                detail: `${item.doctorName} · ${item.status}`,
                to: "/appointments",
              })),
          ),
        ];

        if (user?.role === "Admin" || user?.role === "Doctor") {
          requests.push(
            api.get<Patient[]>("/patients").then((res) =>
              res.data
                .filter(
                  (item) =>
                    item.name.toLowerCase().includes(term.toLowerCase()) ||
                    item.contact.toLowerCase().includes(term.toLowerCase()) ||
                    item.email.toLowerCase().includes(term.toLowerCase()) ||
                    item.uhid.toLowerCase().includes(term.toLowerCase()),
                )
                .slice(0, 6)
                .map((item) => ({
                  id: `patient-${item.id}`,
                  kind: "Patient" as const,
                  title: item.name,
                  detail: `${item.uhid || "No UHID"} · ${item.contact || "No number"}`,
                  to: "/patients",
                })),
            ),
            api.get<DashboardStats>("/dashboard", { params: { from, to: today, page: 1, pageSize: 40 } }).then((res) =>
              res.data.actionItems
                .filter(
                  (item: CallLog) =>
                    item.callerName.toLowerCase().includes(term.toLowerCase()) ||
                    item.callerPhone.toLowerCase().includes(term.toLowerCase()) ||
                    (item.bookedDoctorName ?? "").toLowerCase().includes(term.toLowerCase()) ||
                    item.summary.toLowerCase().includes(term.toLowerCase()),
                )
                .slice(0, 6)
                .map((item) => ({
                  id: `call-${item.id}`,
                  kind: "Call" as const,
                  title: item.callerName,
                  detail: item.bookedDoctorName ? `Booked with ${item.bookedDoctorName}` : item.summary,
                  to: "/calls",
                })),
            ),
          );
        }

        const groups = await Promise.all(requests);
        if (!cancelled) {
          setHits(groups.flat());
          setOpen(true);
        }
      } catch {
        if (!cancelled) setHits([]);
      } finally {
        if (!cancelled) setBusy(false);
      }
    }, 250);

    return () => {
      cancelled = true;
      window.clearTimeout(timer);
    };
  }, [query, user?.role]);

  const empty = useMemo(() => query.trim().length >= 2 && !busy && hits.length === 0, [query, busy, hits.length]);

  function icon(kind: Hit["kind"]) {
    if (kind === "Patient") return UserRound;
    if (kind === "Call") return PhoneCall;
    return CalendarCheck;
  }

  if (compact && !expanded) {
    return (
      <button
        className="rounded-full border border-slate-200 p-2"
        aria-label="Open search"
        onClick={() => setExpanded(true)}
      >
        <Search className="h-4 w-4 text-slate-600" />
      </button>
    );
  }

  return (
    <div ref={boxRef} className={`relative ${compact ? "w-[min(70vw,16rem)]" : "w-full max-w-xs"}`}>
      <div className="flex items-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-3 py-2 text-sm text-slate-600">
        <Search className="h-4 w-4 shrink-0" />
        <input
          className="w-full border-0 bg-transparent p-0 text-sm shadow-none outline-none ring-0 focus:border-0 focus:ring-0"
          value={query}
          autoFocus={compact}
          placeholder="Search patients, appointments..."
          onChange={(e) => setQuery(e.target.value)}
          onFocus={() => query.trim().length >= 2 && setOpen(true)}
        />
        {(query || compact) && (
          <button
            className="rounded-full p-0.5 text-slate-400 hover:text-slate-700"
            aria-label="Clear search"
            onClick={() => {
              setQuery("");
              setHits([]);
              setOpen(false);
              if (compact) setExpanded(false);
            }}
          >
            <X className="h-3.5 w-3.5" />
          </button>
        )}
      </div>
      {open && (hits.length > 0 || empty || busy) && (
        <div className="absolute right-0 z-50 mt-2 max-h-80 w-[min(90vw,22rem)] overflow-y-auto rounded-2xl border border-slate-200 bg-white shadow-xl">
          {busy && <p className="px-4 py-3 text-sm text-slate-500">Searching…</p>}
          {empty && <p className="px-4 py-3 text-sm text-slate-500">No matches found.</p>}
          {hits.map((hit) => {
            const Icon = icon(hit.kind);
            return (
              <button
                key={hit.id}
                className="flex w-full items-start gap-3 border-b border-slate-100 px-4 py-3 text-left hover:bg-teal-50"
                onClick={() => {
                  navigate(hit.to);
                  setOpen(false);
                  setQuery("");
                  if (compact) setExpanded(false);
                }}
              >
                <span className="mt-0.5 rounded-xl bg-teal-50 p-2 text-teal-800">
                  <Icon className="h-4 w-4" />
                </span>
                <span className="min-w-0">
                  <span className="block text-xs font-semibold uppercase tracking-[0.14em] text-teal-700">{hit.kind}</span>
                  <span className="block truncate font-medium text-slate-800">{hit.title}</span>
                  <span className="block truncate text-xs text-slate-500">{hit.detail}</span>
                </span>
              </button>
            );
          })}
        </div>
      )}
    </div>
  );
}
