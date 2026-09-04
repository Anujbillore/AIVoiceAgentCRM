import { useEffect, useRef, useState, type FormEvent } from "react";
import { Mic, PhoneCall, PhoneOff, Square } from "lucide-react";
import { api, type VoiceSession, type VoiceStatus } from "../api/client";

const samples = [
  "I need an appointment with Dr. Mehta at 5 PM today.",
  "What are your clinic visiting hours this week?",
  "What's my billing balance?",
  "I want to speak to a person.",
  "This is an emergency, chest pain.",
  "Congratulations you have won a lottery prize.",
];

function speakFallback(text: string) {
  if (!text || !("speechSynthesis" in window)) return;
  const utterance = new SpeechSynthesisUtterance(text);
  utterance.rate = 1;
  window.speechSynthesis.cancel();
  window.speechSynthesis.speak(utterance);
}

function playReply(session: VoiceSession) {
  if (session.audioBase64) {
    const audio = new Audio(`data:audio/wav;base64,${session.audioBase64}`);
    void audio.play();
    return;
  }
  speakFallback(session.replyText);
}

export function VoiceAgentPage() {
  const [status, setStatus] = useState<VoiceStatus | null>(null);
  const [callerName, setCallerName] = useState("Rahul Sharma");
  const [callerPhone, setCallerPhone] = useState("+91 90000 10001");
  const [spokenText, setSpokenText] = useState("");
  const [session, setSession] = useState<VoiceSession | null>(null);
  const [loading, setLoading] = useState(false);
  const [recording, setRecording] = useState(false);
  const [error, setError] = useState("");
  const mediaRef = useRef<MediaRecorder | null>(null);
  const chunksRef = useRef<Blob[]>([]);

  useEffect(() => {
    void api.get<VoiceStatus>("/voice/status").then(({ data }) => setStatus(data));
  }, []);

  function apply(next: VoiceSession) {
    setSession(next);
    playReply(next);
  }

  async function startCall() {
    setError("");
    setLoading(true);
    try {
      const { data } = await api.post<VoiceSession>("/voice/session", { callerName, callerPhone });
      apply(data);
    } catch {
      setError("Could not start the voice session.");
    } finally {
      setLoading(false);
    }
  }

  async function sendTurn(text: string) {
    if (!session || session.ended || !text.trim()) return;
    setLoading(true);
    setError("");
    try {
      const { data } = await api.post<VoiceSession>(`/voice/session/${session.sessionId}/turn`, { spokenText: text });
      apply(data);
      setSpokenText("");
    } catch (err: unknown) {
      const axiosErr = err as { response?: { data?: { message?: string } } };
      setError(axiosErr.response?.data?.message ?? "The agent could not process that turn.");
    } finally {
      setLoading(false);
    }
  }

  async function hangup() {
    if (!session || session.ended) return;
    setLoading(true);
    try {
      const { data } = await api.post<VoiceSession>(`/voice/session/${session.sessionId}/end`);
      apply(data);
    } finally {
      setLoading(false);
    }
  }

  async function toggleRecord() {
    if (recording && mediaRef.current) {
      mediaRef.current.stop();
      setRecording(false);
      return;
    }

    try {
      const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
      const recorder = new MediaRecorder(stream);
      chunksRef.current = [];
      recorder.ondataavailable = (event) => {
        if (event.data.size > 0) chunksRef.current.push(event.data);
      };
      recorder.onstop = async () => {
        stream.getTracks().forEach((track) => track.stop());
        const blob = new Blob(chunksRef.current, { type: recorder.mimeType || "audio/webm" });
        if (session && !session.ended && status?.sarvamConfigured) {
          const form = new FormData();
          form.append("file", blob, "turn.webm");
          setLoading(true);
          try {
            const { data } = await api.post<VoiceSession>(`/voice/session/${session.sessionId}/audio`, form);
            apply(data);
          } catch (err: unknown) {
            const axiosErr = err as { response?: { data?: { message?: string } } };
            setError(axiosErr.response?.data?.message ?? "Could not transcribe that clip.");
          } finally {
            setLoading(false);
          }
          return;
        }

        try {
          const { data } = await api.post<{ transcript: string }>("/voice/transcribe", (() => {
            const form = new FormData();
            form.append("file", blob, "call.webm");
            return form;
          })());
          setSpokenText(data.transcript);
        } catch (err: unknown) {
          const axiosErr = err as { response?: { data?: { message?: string } } };
          setError(axiosErr.response?.data?.message ?? "Type the caller’s words if speech-to-text is not configured.");
        }
      };
      mediaRef.current = recorder;
      recorder.start();
      setRecording(true);
      setError("");
    } catch {
      setError("Microphone access was denied.");
    }
  }

  function submitTurn(e: FormEvent) {
    e.preventDefault();
    void sendTurn(spokenText);
  }

  return (
    <div className="space-y-6">
      <section className="card space-y-4 p-5">
        <h2 className="font-display text-2xl">Go live with Exotel + Sarvam</h2>
        <p className="text-sm text-slate-600">
          Phone calls use <strong>Exotel</strong> for the number and <strong>Sarvam</strong> for speech-to-text, intent, and spoken replies.
          The same clinic engine then books, answers, or escalates.
        </p>
        <ol className="grid gap-3 text-sm md:grid-cols-2 xl:grid-cols-3">
          <li className="rounded-2xl bg-slate-50 p-4">
            <p className="font-semibold text-teal-800">1. Sarvam API key</p>
            <p className="mt-1 text-slate-600">
              From dashboard.sarvam.ai put <code>Sarvam:ApiKey</code> in <code>appsettings.Local.json</code>, then restart the API. Phone speech will not work without this.
            </p>
          </li>
          <li className="rounded-2xl bg-slate-50 p-4">
            <p className="font-semibold text-teal-800">2. Exotel number</p>
            <p className="mt-1 text-slate-600">
              In my.exotel.com get an ExoPhone. Add <code>Exotel:ApiKey</code> and <code>Exotel:ApiToken</code> so private recordings can be downloaded.
            </p>
          </li>
          <li className="rounded-2xl bg-slate-50 p-4">
            <p className="font-semibold text-teal-800">3. Public HTTPS URL</p>
            <p className="mt-1 text-slate-600">
              Run <code>ngrok http 5147</code>. Set <code>Voice:PublicBaseUrl</code> to that https URL so Exotel can play Sarvam TTS.
            </p>
          </li>
          <li className="rounded-2xl bg-slate-50 p-4">
            <p className="font-semibold text-teal-800">4. Point the ExoPhone</p>
            <p className="mt-1 text-slate-600">
              App Bazaar → Create App → URL / ExoML → paste the Exotel incoming webhook (ngrok host). Assign that app to the ExoPhone.
            </p>
          </li>
          <li className="rounded-2xl bg-slate-50 p-4">
            <p className="font-semibold text-teal-800">5. Test in this page</p>
            <p className="mt-1 text-slate-600">Start a live call here first. When Sarvam shows Connected, dial the ExoPhone and book an appointment.</p>
          </li>
          <li className="rounded-2xl bg-slate-50 p-4">
            <p className="font-semibold text-teal-800">6. Optional Twilio</p>
            <p className="mt-1 text-slate-600">Keep Twilio only if you also need a non-India trial number. Production in India is Exotel + Sarvam.</p>
          </li>
        </ol>
        {status && (
          <div className="grid gap-3 rounded-2xl border border-slate-100 p-4 text-sm md:grid-cols-2">
            <p>
              <span className="text-slate-500">Phone stack:</span>{" "}
              <strong>{status.phoneReady ? "Ready — Exotel can use Sarvam STT/TTS" : "Blocked — add Sarvam:ApiKey and restart"}</strong>
            </p>
            <p>
              <span className="text-slate-500">Sarvam AI:</span>{" "}
              <strong>{status.sarvamConfigured ? "Connected" : "Not configured"}</strong>
            </p>
            <p>
              <span className="text-slate-500">Agent:</span> <strong>{status.agentName}</strong>
            </p>
            <p>
              <span className="text-slate-500">Exotel API:</span>{" "}
              <strong>{status.exotelConfigured ? "Credentials set" : "Not set — public recordings only"}</strong>
            </p>
            <p className="md:col-span-2 break-all">
              <span className="text-slate-500">Exotel incoming (ExoML):</span> {status.exotelIncomingWebhook}
            </p>
            <p className="md:col-span-2 break-all">
              <span className="text-slate-500">Exotel recording callback:</span> {status.exotelTurnWebhook}
            </p>
            <p className="md:col-span-2 break-all">
              <span className="text-slate-500">Exotel TTS playback:</span> {status.exotelAudioWebhook}/{"{sessionId}"}
            </p>
            <p className="md:col-span-2 break-all">
              <span className="text-slate-500">Twilio incoming webhook:</span> {status.incomingWebhook}
            </p>
            <p className="md:col-span-2 break-all">
              <span className="text-slate-500">Twilio gather webhook:</span> {status.gatherWebhook}
            </p>
          </div>
        )}
      </section>

      <div className="grid gap-6 xl:grid-cols-[1.1fr_0.9fr]">
        <div className="card space-y-4 p-5">
          <div className="flex items-center gap-3">
            <div className="flex h-10 w-10 items-center justify-center rounded-2xl bg-teal-100 text-teal-800">
              <PhoneCall className="h-5 w-5" />
            </div>
            <div>
              <h2 className="font-display text-xl">Live clinic call</h2>
              <p className="text-sm text-slate-500">Greeting → listen → intent → book / query / spam → speak → dashboard.</p>
            </div>
          </div>
          {error && <p className="rounded-xl bg-rose-50 px-3 py-2 text-sm text-rose-700">{error}</p>}
          <div className="grid gap-3 sm:grid-cols-2">
            <div>
              <label>Caller name</label>
              <input value={callerName} onChange={(e) => setCallerName(e.target.value)} disabled={!!session && !session.ended} />
            </div>
            <div>
              <label>Caller phone</label>
              <input value={callerPhone} onChange={(e) => setCallerPhone(e.target.value)} disabled={!!session && !session.ended} />
            </div>
          </div>
          {!session || session.ended ? (
            <button className="btn-primary" disabled={loading} onClick={() => void startCall()}>
              {loading ? "Connecting…" : "Start live call"}
            </button>
          ) : (
            <form onSubmit={submitTurn} className="space-y-3">
              <div className="flex flex-wrap gap-2">
                {samples.map((sample) => (
                  <button key={sample} type="button" className="btn-ghost text-xs" onClick={() => setSpokenText(sample)}>
                    {sample.slice(0, 28)}…
                  </button>
                ))}
              </div>
              <div>
                <label>Your turn</label>
                <textarea rows={3} value={spokenText} onChange={(e) => setSpokenText(e.target.value)} placeholder="Speak or type what the caller says" />
              </div>
              <div className="flex flex-wrap gap-2">
                <button className="btn-primary" disabled={loading || !spokenText.trim()}>
                  {loading ? "Agent is thinking…" : "Send to agent"}
                </button>
                <button type="button" className="btn-ghost" disabled={loading} onClick={() => void toggleRecord()}>
                  {recording ? <Square className="h-4 w-4" /> : <Mic className="h-4 w-4" />}
                  {recording ? "Stop" : "Hold mic"}
                </button>
                <button type="button" className="btn-danger" disabled={loading} onClick={() => void hangup()}>
                  <PhoneOff className="h-4 w-4" />
                  Hang up
                </button>
              </div>
            </form>
          )}
          {session?.ended && (
            <button className="btn-ghost" onClick={() => setSession(null)}>
              Start another call
            </button>
          )}
        </div>

        <div className="card space-y-4 p-5">
          <h2 className="font-display text-xl">Conversation</h2>
          {!session && <p className="text-sm text-slate-500">Start a live call to hear the greeting and talk through booking or a query.</p>}
          {session && (
            <>
              <div className="space-y-2">
                {session.transcript.map((turn, index) => (
                  <div
                    key={`${turn.role}-${index}`}
                    className={`rounded-2xl px-3 py-2 text-sm ${turn.role === "agent" ? "bg-teal-50 text-teal-950" : "bg-slate-100 text-slate-800"}`}
                  >
                    <p className="text-xs font-semibold uppercase tracking-wide opacity-70">{turn.role}</p>
                    <p>{turn.text}</p>
                  </div>
                ))}
              </div>
              <dl className="space-y-2 text-sm">
                <div>
                  <dt className="text-slate-500">Intent</dt>
                  <dd>
                    <span className="rounded-full bg-teal-50 px-2 py-1 text-xs font-semibold text-teal-800">{session.intent}</span>
                  </dd>
                </div>
                <div className="grid grid-cols-2 gap-2">
                  <div>
                    <dt className="text-slate-500">Phase</dt>
                    <dd>{session.phase ?? "Greeting"}</dd>
                  </div>
                  <div>
                    <dt className="text-slate-500">Confidence</dt>
                    <dd>{Math.round((session.confidence ?? 1) * 100)}%</dd>
                  </div>
                  <div>
                    <dt className="text-slate-500">Outcome</dt>
                    <dd>{session.outcome || (session.ended ? "Closed" : "In progress")}</dd>
                  </div>
                  <div>
                    <dt className="text-slate-500">Transfer</dt>
                    <dd>{session.transferType ?? "None"}</dd>
                  </div>
                </div>
                {session.escalationReason && (
                  <div className="rounded-xl bg-amber-50 p-3 text-amber-900">
                    Escalation: {session.escalationReason}
                    {session.isVip ? " · VIP caller" : ""}
                    {session.afterHours ? " · after hours" : ""}
                  </div>
                )}
                <div>
                  <dt className="text-slate-500">Action taken</dt>
                  <dd>{session.actionTaken}</dd>
                </div>
                {session.appointment && (
                  <div className="rounded-xl bg-slate-50 p-3">
                    Booked {session.appointment.patientName} with {session.appointment.doctorName} at{" "}
                    {new Date(session.appointment.scheduledAt).toLocaleString()}. Doctor email sent.
                  </div>
                )}
              </dl>
            </>
          )}
        </div>
      </div>
    </div>
  );
}
