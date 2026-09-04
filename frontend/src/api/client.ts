import axios from "axios";

const TOKEN_KEY = "clinic.token";

export const api = axios.create({
  baseURL: "/api",
});

api.interceptors.request.use((config) => {
  const token = localStorage.getItem(TOKEN_KEY);
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

api.interceptors.response.use(
  (response) => response,
  (error) => {
    const url = error.config?.url ?? "";
    const publicAuth = ["/auth/login", "/auth/register", "/auth/forgot-password", "/auth/reset-password"];
    if (error.response?.status === 401 && !publicAuth.some((path) => url.includes(path))) {
      localStorage.removeItem(TOKEN_KEY);
      if (window.location.pathname !== "/login") {
        window.location.href = "/login";
      }
    }
    return Promise.reject(error);
  },
);

export type Role = "Admin" | "Doctor" | "Patient";

export interface AuthUser {
  token: string;
  email: string;
  fullName: string;
  role: Role;
  expiresAt: string;
}

export interface Patient {
  id: number;
  name: string;
  age: number;
  contact: string;
  email: string;
  address: string;
  notes: string;
  createdAt: string;
  visitCount: number;
  documentCount: number;
  uhid: string;
  gender: string;
  bloodGroup: string;
  emergencyName: string;
  emergencyPhone: string;
  allergies: string;
}

export interface PatientDocument {
  id: number;
  patientId: number;
  appointmentId: number | null;
  category: string;
  originalName: string;
  contentType: string;
  sizeBytes: number;
  uploadedAt: string;
}

export interface PatientCharge {
  id: number;
  patientId: number;
  appointmentId: number | null;
  kind: string;
  title: string;
  amount: number;
  notes: string;
  chargeDate: string;
}

export interface PatientDetail {
  patient: Patient;
  visits: Appointment[];
  documents: PatientDocument[];
  charges: PatientCharge[];
}

export const DOCUMENT_FOLDERS = [
  { id: "Aadhaar", label: "Aadhaar / ID", hint: "Aadhaar, PAN, passport" },
  { id: "Reports", label: "Reports", hint: "Lab, scan, X-ray, MRI" },
  { id: "Prescriptions", label: "Prescriptions", hint: "Rx and medicines" },
  { id: "Insurance", label: "Insurance", hint: "Policy, TPA, claims" },
  { id: "Billing", label: "Billing", hint: "Final bills, invoices, receipts" },
  { id: "Daily bills", label: "Daily bills", hint: "Ward and daily charges" },
  { id: "Discharge", label: "Discharge", hint: "Admission and discharge notes" },
  { id: "Consent", label: "Consent", hint: "Consent and OT forms" },
  { id: "Other", label: "Other", hint: "Unsorted records" },
] as const;

export const DOCUMENT_CATEGORIES = DOCUMENT_FOLDERS.map((folder) => folder.id);

export interface Schedule {
  id: number;
  dayOfWeek: number;
  startTime: string;
  endTime: string;
}

export interface Doctor {
  id: number;
  name: string;
  email: string;
  specialization: string;
  phone: string;
  isActive: boolean;
  hasLogin: boolean;
  schedules: Schedule[];
}

export interface AppointmentPage {
  items: Appointment[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Appointment {
  id: number;
  patientId: number;
  patientName: string;
  patientAge: number;
  patientContact: string;
  doctorId: number;
  doctorName: string;
  doctorEmail: string;
  scheduledAt: string;
  status: string;
  notes: string;
  createdAt: string;
}

export interface CallLog {
  id: number;
  callerName: string;
  callerPhone: string;
  summary: string;
  actionTaken: string;
  intent: string;
  transcript: string;
  timestamp: string;
  outcome?: string;
  escalationReason?: string;
  transferType?: string;
  confidence?: number;
  sentiment?: string;
  consentGiven?: boolean;
}

export interface EmailMessage {
  id: number;
  recipient: string;
  subject: string;
  body: string;
  sentAt: string;
  delivery: string;
}

export interface AppointmentStatusDay {
  label: string;
  booked: number;
  pending: number;
  completed: number;
  cancelled: number;
}

export interface DashboardStats {
  fromDate: string;
  toDate: string;
  callsToday: number;
  appointmentsToday: number;
  upcomingAppointments: number;
  activeDoctors: number;
  bookedAppointments: number;
  pendingAppointments: number;
  completedAppointments: number;
  cancelledAppointments: number;
  callVolume: { label: string; count: number }[];
  appointmentStats: { label: string; count: number }[];
  appointmentStatusByDay: AppointmentStatusDay[];
  actionItems: CallLog[];
  actionItemTotal: number;
  actionItemPage: number;
  actionItemPageSize: number;
  containmentRate?: number;
  escalatedCalls?: number;
  callbackQueued?: number;
  recentEmails: EmailMessage[];
}

export interface SystemStatus {
  provider: string;
  host: string;
  connected: boolean;
  patients: number;
  doctors: number;
  appointments: number;
  callLogs: number;
  emails: number;
}

export interface AiSettings {
  id: number;
  agentName: string;
  welcomeMessage: string;
  language: string;
  instructions: string;
  voiceSpeaker: string;
  consentMessage?: string;
  transferNumber?: string;
}

export interface VoiceTurn {
  agentName: string;
  welcomeMessage: string;
  replyText: string;
  intent: string;
  summary: string;
  actionTaken: string;
  callLog: CallLog;
  appointment: Appointment | null;
  audioBase64: string | null;
}

export interface VoiceChatTurn {
  role: string;
  text: string;
  intent: string;
}

export interface VoiceSession {
  sessionId: string;
  agentName: string;
  welcomeMessage: string;
  replyText: string;
  intent: string;
  summary: string;
  actionTaken: string;
  ended: boolean;
  callLog: CallLog | null;
  appointment: Appointment | null;
  audioBase64: string | null;
  transcript: VoiceChatTurn[];
  phase?: string;
  confidence?: number;
  outcome?: string;
  escalationReason?: string;
  transferType?: string;
  transferNumber?: string | null;
  afterHours?: boolean;
  consentGiven?: boolean;
  clarifyingQuestions?: number;
  isVip?: boolean;
}

export interface VoiceStatus {
  sarvamConfigured: boolean;
  agentName: string;
  welcomeMessage: string;
  incomingWebhook: string;
  gatherWebhook: string;
  languages: string[];
  exotelIncomingWebhook: string;
  exotelTurnWebhook: string;
  exotelAudioWebhook: string;
  exotelConfigured: boolean;
  publicBaseUrl?: string;
  phoneReady?: boolean;
}

export interface InvoiceLine {
  id: number;
  description: string;
  quantity: number;
  unitPrice: number;
  amount: number;
}

export interface Invoice {
  id: number;
  number: string;
  patientId: number;
  patientName: string;
  patientUhid: string;
  appointmentId: number | null;
  status: string;
  issuedAt: string;
  dueAt: string | null;
  notes: string;
  subtotal: number;
  tax: number;
  discount: number;
  total: number;
  paidAmount: number;
  refundedAmount: number;
  balance: number;
  lines: InvoiceLine[];
}

export interface PaymentSplit {
  method: string;
  amount: number;
}

export interface Refund {
  id: number;
  amount: number;
  reason: string;
  status: string;
  refundedAt: string;
}

export interface Payment {
  id: number;
  receiptNumber: string;
  patientId: number;
  patientName: string;
  invoiceId: number | null;
  invoiceNumber: string | null;
  amount: number;
  status: string;
  gateway: string;
  reference: string;
  notes: string;
  paidAt: string;
  refundedAmount: number;
  splits: PaymentSplit[];
  refunds: Refund[];
}

export interface InsurancePolicy {
  id: number;
  patientId: number;
  patientName: string;
  provider: string;
  policyNumber: string;
  tpa: string;
  memberId: string;
  validFrom: string;
  validTo: string;
  coverageLimit: number;
  status: string;
  eligibility: string;
  eligibilityNotes: string;
  eligibilityCheckedAt: string | null;
}

export interface InsuranceClaim {
  id: number;
  claimNumber: string;
  policyId: number;
  provider: string;
  policyNumber: string;
  patientId: number;
  patientName: string;
  invoiceId: number | null;
  invoiceNumber: string | null;
  amount: number;
  status: string;
  notes: string;
  createdAt: string;
  submittedAt: string | null;
}

export async function downloadPatientDocument(patientId: number, documentId: number, fileName: string) {
  const { data } = await api.get<Blob>(`/patients/${patientId}/documents/${documentId}/file`, { responseType: "blob" });
  const url = URL.createObjectURL(data);
  const link = document.createElement("a");
  link.href = url;
  link.download = fileName;
  document.body.appendChild(link);
  link.click();
  link.remove();
  URL.revokeObjectURL(url);
}

export { TOKEN_KEY };
