import { useMemo, useState } from "react";
import { FileUp } from "lucide-react";
import { api, DOCUMENT_FOLDERS, downloadPatientDocument, type Appointment, type PatientDocument } from "../api/client";

function formatStamp(value: string) {
  return new Date(value).toLocaleString([], { dateStyle: "medium", timeStyle: "short" });
}

function formatSize(bytes: number) {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

export function PatientDocumentsPanel({
  patientId,
  documents,
  visits = [],
  onChanged,
}: {
  patientId: number;
  documents: PatientDocument[];
  visits?: Appointment[];
  onChanged: () => Promise<void> | void;
}) {
  const [folder, setFolder] = useState("Reports");
  const [sortMode, setSortMode] = useState("auto");
  const [visitId, setVisitId] = useState("");
  const [uploading, setUploading] = useState(false);
  const [message, setMessage] = useState("");

  const folders = useMemo(() => {
    const known = DOCUMENT_FOLDERS.map((item) => item.id as string);
    const extras = documents.map((doc) => doc.category).filter((category) => !known.includes(category));
    return [...DOCUMENT_FOLDERS, ...[...new Set(extras)].map((id) => ({ id, label: id, hint: "Imported folder" }))];
  }, [documents]);

  const visible = documents.filter((doc) => doc.category === folder);
  const active = folders.find((item) => item.id === folder);

  async function upload(files: FileList | null) {
    if (!files?.length) return;
    setUploading(true);
    setMessage("");
    try {
      let lastCategory = folder;
      for (const file of Array.from(files)) {
        const data = new FormData();
        data.append("file", file);
        if (sortMode !== "auto") {
          data.append("category", sortMode);
        }
        if (visitId) {
          data.append("appointmentId", visitId);
        }
        const { data: saved } = await api.post<PatientDocument>(`/patients/${patientId}/documents`, data);
        lastCategory = saved.category;
      }
      setFolder(lastCategory);
      setMessage(sortMode === "auto" ? `Uploaded and filed under ${lastCategory}.` : `Uploaded to ${sortMode}.`);
      await onChanged();
    } catch {
      setMessage("Upload failed. Use PDF, JPG, PNG, WEBP, DOC, or DOCX under 10 MB.");
    } finally {
      setUploading(false);
    }
  }

  async function remove(doc: PatientDocument) {
    await api.delete(`/patients/${patientId}/documents/${doc.id}`);
    await onChanged();
  }

  async function move(doc: PatientDocument, category: string) {
    if (category === doc.category) return;
    await api.patch(`/patients/${patientId}/documents/${doc.id}`, { category });
    setFolder(category);
    await onChanged();
  }

  return (
    <section className="card space-y-5 p-5">
      <div>
        <h3 className="font-display text-xl">Patient records</h3>
        <p className="text-sm text-slate-500">
          Same pattern as clinic CRMs: files live in a chart folder and can be attached to a visit.
        </p>
      </div>

      <div className="flex flex-wrap gap-2">
        {folders.map((item) => {
          const count = documents.filter((doc) => doc.category === item.id).length;
          const selected = folder === item.id;
          return (
            <button
              key={item.id}
              type="button"
              onClick={() => setFolder(item.id)}
              className={`rounded-full px-3 py-1.5 text-sm font-medium transition ${
                selected ? "bg-teal-700 text-white" : "bg-slate-100 text-slate-600 hover:bg-slate-200"
              }`}
            >
              {item.label}
              <span className={`ml-1.5 text-xs ${selected ? "text-teal-100" : "text-slate-400"}`}>{count}</span>
            </button>
          );
        })}
      </div>

      <div className="grid gap-3 sm:grid-cols-2 xl:grid-cols-[200px_220px_1fr]">
        <div>
          <label>File into</label>
          <select value={sortMode} onChange={(e) => setSortMode(e.target.value)}>
            <option value="auto">Let AI sort</option>
            {DOCUMENT_FOLDERS.map((item) => (
              <option key={item.id} value={item.id}>
                {item.label}
              </option>
            ))}
          </select>
        </div>
        <div>
          <label>Attach to visit</label>
          <select value={visitId} onChange={(e) => setVisitId(e.target.value)}>
            <option value="">Patient chart only</option>
            {visits.map((visit) => (
              <option key={visit.id} value={visit.id}>
                {new Date(visit.scheduledAt).toLocaleDateString()} · {visit.doctorName}
              </option>
            ))}
          </select>
        </div>
        <label className="flex cursor-pointer flex-col items-center justify-center rounded-2xl border border-dashed border-teal-200 bg-teal-50/40 px-4 py-6 text-center">
          <FileUp className="mb-2 h-6 w-6 text-teal-700" />
          <span className="font-medium text-teal-800">{uploading ? "Uploading…" : "Drop files or click to upload"}</span>
          <span className="mt-1 text-xs text-slate-500">PDF, images, or Word · max 10 MB</span>
          <input
            type="file"
            multiple
            className="hidden"
            accept=".pdf,.jpg,.jpeg,.png,.webp,.doc,.docx"
            disabled={uploading}
            onChange={(e) => {
              void upload(e.target.files);
              e.target.value = "";
            }}
          />
        </label>
      </div>
      {message && <p className="text-sm text-slate-600">{message}</p>}

      <div className="rounded-2xl border border-slate-100 p-4">
        <div className="mb-3">
          <h4 className="font-display text-lg">{active?.label ?? folder}</h4>
          <p className="text-sm text-slate-500">{active?.hint}</p>
        </div>
        {visible.length === 0 ? (
          <p className="py-6 text-sm text-slate-400">No files in this folder yet.</p>
        ) : (
          <ul className="space-y-2">
            {visible.map((doc) => (
              <li key={doc.id} className="flex flex-wrap items-center justify-between gap-3 rounded-xl bg-slate-50 px-3 py-2">
                <div className="min-w-0">
                  <p className="truncate text-sm font-medium">{doc.originalName}</p>
                  <p className="text-xs text-slate-500">
                    {formatSize(doc.sizeBytes)} · {formatStamp(doc.uploadedAt)}
                    {doc.appointmentId
                      ? ` · Visit ${visits.find((visit) => visit.id === doc.appointmentId)?.doctorName ?? ""}`
                      : " · Chart file"}
                  </p>
                </div>
                <div className="flex flex-wrap items-center gap-2">
                  <select
                    className="w-auto min-w-[140px] py-1.5 text-xs"
                    value={doc.appointmentId ?? 0}
                    onChange={(e) => void api.patch(`/patients/${patientId}/documents/${doc.id}`, { appointmentId: Number(e.target.value) }).then(() => onChanged())}
                  >
                    <option value={0}>No visit</option>
                    {visits.map((visit) => (
                      <option key={visit.id} value={visit.id}>
                        {new Date(visit.scheduledAt).toLocaleDateString()}
                      </option>
                    ))}
                  </select>
                  <select
                    className="w-auto min-w-[140px] py-1.5 text-xs"
                    value={doc.category}
                    onChange={(e) => void move(doc, e.target.value)}
                  >
                    {DOCUMENT_FOLDERS.map((item) => (
                      <option key={item.id} value={item.id}>
                        Move to {item.label}
                      </option>
                    ))}
                  </select>
                  <button
                    className="text-xs font-semibold text-teal-700 hover:underline"
                    onClick={() => void downloadPatientDocument(patientId, doc.id, doc.originalName)}
                  >
                    Open
                  </button>
                  <button className="text-xs font-semibold text-rose-600 hover:underline" onClick={() => void remove(doc)}>
                    Delete
                  </button>
                </div>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
