import { useEffect, useState, type FormEvent } from "react";
import { api, type AiSettings, type Doctor, type SystemStatus, type VoiceStatus } from "../api/client";

const DAYS = [
  { value: 0, label: "Sunday" },
  { value: 1, label: "Monday" },
  { value: 2, label: "Tuesday" },
  { value: 3, label: "Wednesday" },
  { value: 4, label: "Thursday" },
  { value: 5, label: "Friday" },
  { value: 6, label: "Saturday" },
];

const LANGUAGES = [
  { value: "en-IN", label: "English (India)" },
  { value: "hi-IN", label: "Hindi" },
  { value: "ta-IN", label: "Tamil" },
  { value: "te-IN", label: "Telugu" },
  { value: "mr-IN", label: "Marathi" },
  { value: "bn-IN", label: "Bengali" },
  { value: "kn-IN", label: "Kannada" },
  { value: "gu-IN", label: "Gujarati" },
];

type DayHours = { enabled: boolean; startTime: string; endTime: string };

function defaultDays(): Record<number, DayHours> {
  return Object.fromEntries(
    DAYS.map((day) => [
      day.value,
      { enabled: day.value >= 1 && day.value <= 6, startTime: "09:00", endTime: "18:00" },
    ]),
  ) as Record<number, DayHours>;
}

const emptyDoctor = {
  name: "",
  email: "",
  specialization: "",
  phone: "",
  password: "",
  isActive: true,
  days: defaultDays(),
};

export function SettingsPage() {
  const [doctors, setDoctors] = useState<Doctor[]>([]);
  const [ai, setAi] = useState<AiSettings | null>(null);
  const [doctorForm, setDoctorForm] = useState(emptyDoctor);
  const [editingId, setEditingId] = useState<number | null>(null);
  const [saved, setSaved] = useState("");
  const [doctorError, setDoctorError] = useState("");
  const [system, setSystem] = useState<SystemStatus | null>(null);
  const [voice, setVoice] = useState<VoiceStatus | null>(null);

  async function load() {
    const [doc, settings, status, voiceStatus] = await Promise.all([
      api.get<Doctor[]>("/doctors"),
      api.get<AiSettings>("/settings/ai"),
      api.get<SystemStatus>("/settings/system"),
      api.get<VoiceStatus>("/voice/status"),
    ]);
    setDoctors(doc.data);
    setAi(settings.data);
    setSystem(status.data);
    setVoice(voiceStatus.data);
  }

  useEffect(() => {
    void load();
  }, []);

  async function saveDoctor(e: FormEvent) {
    e.preventDefault();
    setDoctorError("");
    const schedules = DAYS.filter((day) => doctorForm.days[day.value].enabled).map((day) => ({
      dayOfWeek: day.value,
      startTime: doctorForm.days[day.value].startTime,
      endTime: doctorForm.days[day.value].endTime,
    }));
    try {
      const payload = {
        name: doctorForm.name,
        email: doctorForm.email,
        specialization: doctorForm.specialization,
        phone: doctorForm.phone,
        isActive: doctorForm.isActive,
        password: doctorForm.password || undefined,
        schedules,
      };
      if (editingId) await api.put(`/doctors/${editingId}`, payload);
      else await api.post("/doctors", payload);
      setDoctorForm(emptyDoctor);
      setEditingId(null);
      await load();
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setDoctorError(axiosErr.response?.data?.message ?? "Could not save doctor.");
    }
  }

  async function removeDoctor(id: number) {
    await api.delete(`/doctors/${id}`);
    await load();
  }

  function editDoctor(doctor: Doctor) {
    const days = defaultDays();
    DAYS.forEach((day) => {
      const match = doctor.schedules.find((s) => s.dayOfWeek === day.value);
      days[day.value] = match
        ? { enabled: true, startTime: match.startTime, endTime: match.endTime }
        : { enabled: false, startTime: "09:00", endTime: "18:00" };
    });
    setEditingId(doctor.id);
    setDoctorForm({
      name: doctor.name,
      email: doctor.email,
      specialization: doctor.specialization,
      phone: doctor.phone,
      password: "",
      isActive: doctor.isActive,
      days,
    });
    setDoctorError("");
  }

  async function saveAi(e: FormEvent) {
    e.preventDefault();
    if (!ai) return;
    await api.put("/settings/ai", ai);
    setSaved("AI settings saved.");
    window.setTimeout(() => setSaved(""), 2500);
  }

  const selectedLanguages = (ai?.language ?? "")
    .split(",")
    .map((item) => item.trim())
    .filter(Boolean);

  function toggleLanguage(code: string) {
    if (!ai) return;
    const next = selectedLanguages.includes(code)
      ? selectedLanguages.filter((item) => item !== code)
      : [...selectedLanguages, code];
    setAi({ ...ai, language: next.join(",") });
  }

  return (
    <div className="grid gap-6 xl:grid-cols-2">
      {system && (
        <section className="card xl:col-span-2 p-5">
          <div className="flex flex-wrap items-center justify-between gap-3">
            <div>
              <h2 className="font-display text-xl">Supabase database</h2>
              <p className="text-sm text-slate-500">
                {system.provider} · {system.host}
              </p>
            </div>
            <span
              className={`rounded-full px-3 py-1 text-xs font-semibold ${
                system.connected ? "bg-teal-50 text-teal-800" : "bg-rose-50 text-rose-700"
              }`}
            >
              {system.connected ? "Connected" : "Disconnected"}
            </span>
          </div>
          <div className="mt-4 grid gap-3 sm:grid-cols-5 text-sm">
            <div className="rounded-xl bg-slate-50 p-3"><p className="text-slate-500">Patients</p><p className="font-display text-2xl">{system.patients}</p></div>
            <div className="rounded-xl bg-slate-50 p-3"><p className="text-slate-500">Doctors</p><p className="font-display text-2xl">{system.doctors}</p></div>
            <div className="rounded-xl bg-slate-50 p-3"><p className="text-slate-500">Appointments</p><p className="font-display text-2xl">{system.appointments}</p></div>
            <div className="rounded-xl bg-slate-50 p-3"><p className="text-slate-500">Call logs</p><p className="font-display text-2xl">{system.callLogs}</p></div>
            <div className="rounded-xl bg-slate-50 p-3"><p className="text-slate-500">Emails</p><p className="font-display text-2xl">{system.emails}</p></div>
          </div>
        </section>
      )}
      <section className="space-y-4">
        <form onSubmit={saveDoctor} className="card space-y-3 p-5">
          <h2 className="font-display text-xl">{editingId ? "Edit doctor" : "Add doctor"}</h2>
          <p className="text-sm text-slate-500">Set Gmail and password so the doctor can sign in and see only their appointments.</p>
          {doctorError && <p className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">{doctorError}</p>}
          <div>
            <label>Name</label>
            <input value={doctorForm.name} onChange={(e) => setDoctorForm({ ...doctorForm, name: e.target.value })} required />
          </div>
          <div>
            <label>Gmail / login email</label>
            <input type="email" value={doctorForm.email} onChange={(e) => setDoctorForm({ ...doctorForm, email: e.target.value })} required />
          </div>
          <div>
            <label>{editingId ? "Password (leave blank to keep current)" : "Password"}</label>
            <input
              type="password"
              value={doctorForm.password}
              onChange={(e) => setDoctorForm({ ...doctorForm, password: e.target.value })}
              required={!editingId}
              placeholder="Min 6 characters, one uppercase and one number"
            />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <label>Specialization</label>
              <input value={doctorForm.specialization} onChange={(e) => setDoctorForm({ ...doctorForm, specialization: e.target.value })} />
            </div>
            <div>
              <label>Phone</label>
              <input value={doctorForm.phone} onChange={(e) => setDoctorForm({ ...doctorForm, phone: e.target.value })} />
            </div>
          </div>
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">Working days</p>
            <div className="space-y-2">
              {DAYS.map((day) => {
                const hours = doctorForm.days[day.value];
                return (
                  <div key={day.value} className="flex flex-wrap items-center gap-3 rounded-xl border border-slate-200 px-3 py-2">
                    <label className="flex min-w-28 items-center gap-2 text-sm">
                      <input
                        type="checkbox"
                        className="h-4 w-4"
                        checked={hours.enabled}
                        onChange={(e) =>
                          setDoctorForm({
                            ...doctorForm,
                            days: { ...doctorForm.days, [day.value]: { ...hours, enabled: e.target.checked } },
                          })
                        }
                      />
                      {day.label}
                    </label>
                    <input
                      type="time"
                      className="w-auto"
                      disabled={!hours.enabled}
                      value={hours.startTime}
                      onChange={(e) =>
                        setDoctorForm({
                          ...doctorForm,
                          days: { ...doctorForm.days, [day.value]: { ...hours, startTime: e.target.value } },
                        })
                      }
                    />
                    <span className="text-xs text-slate-400">to</span>
                    <input
                      type="time"
                      className="w-auto"
                      disabled={!hours.enabled}
                      value={hours.endTime}
                      onChange={(e) =>
                        setDoctorForm({
                          ...doctorForm,
                          days: { ...doctorForm.days, [day.value]: { ...hours, endTime: e.target.value } },
                        })
                      }
                    />
                  </div>
                );
              })}
            </div>
          </div>
          <label className="flex items-center gap-2 text-sm">
            <input
              type="checkbox"
              className="h-4 w-4"
              checked={doctorForm.isActive}
              onChange={(e) => setDoctorForm({ ...doctorForm, isActive: e.target.checked })}
            />
            Active
          </label>
          <div className="flex gap-2">
            <button className="btn-primary">{editingId ? "Save doctor & login" : "Add doctor & login"}</button>
            {editingId && (
              <button
                type="button"
                className="btn-ghost"
                onClick={() => {
                  setEditingId(null);
                  setDoctorForm(emptyDoctor);
                  setDoctorError("");
                }}
              >
                Cancel
              </button>
            )}
          </div>
        </form>

        <div className="card overflow-hidden">
          <div className="border-b border-slate-100 px-5 py-4">
            <h2 className="font-display text-xl">Doctors & timings</h2>
          </div>
          <ul className="divide-y divide-slate-100">
            {doctors.map((doctor) => (
              <li key={doctor.id} className="flex items-center justify-between px-5 py-4">
                <div>
                  <p className="font-medium">
                    {doctor.name} {!doctor.isActive && <span className="text-xs text-rose-600">(inactive)</span>}
                  </p>
                  <p className="text-sm text-slate-500">
                    {doctor.specialization} · {doctor.email}
                    {doctor.hasLogin ? " · can sign in" : " · no login yet"}
                  </p>
                </div>
                <div className="text-sm">
                  <button className="text-teal-700 hover:underline" onClick={() => editDoctor(doctor)}>
                    Edit
                  </button>
                  <button className="ml-3 text-rose-600 hover:underline" onClick={() => void removeDoctor(doctor.id)}>
                    Remove
                  </button>
                </div>
              </li>
            ))}
          </ul>
        </div>
      </section>

      {ai && (
        <form onSubmit={saveAi} className="card space-y-3 p-5">
          <h2 className="font-display text-xl">AI agent settings</h2>
          <p className="text-sm text-slate-500">These values drive the voice greeting, languages, and instructions sent to Sarvam AI.</p>
          {voice && (
            <div className="rounded-xl bg-slate-50 px-3 py-2 text-sm text-slate-600">
              Phone stack: {voice.phoneReady ? "Exotel + Sarvam ready" : "add Sarvam:ApiKey"}. Exotel incoming:{" "}
              {voice.exotelIncomingWebhook}
            </div>
          )}
          {saved && <p className="rounded-xl bg-teal-50 px-3 py-2 text-sm text-teal-800">{saved}</p>}
          <div>
            <label>Agent name</label>
            <input value={ai.agentName} onChange={(e) => setAi({ ...ai, agentName: e.target.value })} />
          </div>
          <div>
            <label>Welcome message</label>
            <textarea rows={3} value={ai.welcomeMessage} onChange={(e) => setAi({ ...ai, welcomeMessage: e.target.value })} />
          </div>
          <div>
            <label>Consent / recording notice</label>
            <textarea
              rows={2}
              value={ai.consentMessage ?? ""}
              onChange={(e) => setAi({ ...ai, consentMessage: e.target.value })}
            />
          </div>
          <div>
            <label>Human transfer number (Exotel / Twilio Dial)</label>
            <input
              value={ai.transferNumber ?? ""}
              onChange={(e) => setAi({ ...ai, transferNumber: e.target.value })}
              placeholder="+91xxxxxxxxxx"
            />
          </div>
          <div>
            <p className="mb-2 text-sm font-medium text-slate-600">Languages</p>
            <div className="grid grid-cols-2 gap-2">
              {LANGUAGES.map((lang) => (
                <label key={lang.value} className="flex items-center gap-2 rounded-xl border border-slate-200 px-3 py-2 text-sm">
                  <input
                    type="checkbox"
                    className="h-4 w-4"
                    checked={selectedLanguages.includes(lang.value)}
                    onChange={() => toggleLanguage(lang.value)}
                  />
                  {lang.label}
                </label>
              ))}
            </div>
          </div>
          <div>
            <label>Voice</label>
            <select value={ai.voiceSpeaker} onChange={(e) => setAi({ ...ai, voiceSpeaker: e.target.value })}>
              <option value="ritu">ritu</option>
              <option value="priya">priya</option>
              <option value="shubh">shubh</option>
              <option value="aditya">aditya</option>
            </select>
          </div>
          <div>
            <label>Instructions</label>
            <textarea rows={6} value={ai.instructions} onChange={(e) => setAi({ ...ai, instructions: e.target.value })} />
          </div>
          <button className="btn-primary" disabled={selectedLanguages.length === 0}>
            Save AI settings
          </button>
        </form>
      )}
    </div>
  );
}
