# 🧑‍⚕️ AI Voice Agent Web Portal (End-to-End in .NET)

A complete web portal + backend system for managing **AI-powered voice agents** that handle patient appointments and doctor schedules.

---

## 🔐 Login Page
- ASP.NET Identity authentication
- Role-based access (Admin, Doctor, Patient)
- Password reset via email

---

## 📊 Dashboard
- **Graphs**: Daily call volume, appointment stats
- **Action Items Grid**:
  - Each incoming call → AI generates a **summary**
  - Summary auto‑added via SignalR
  - Columns: Caller, Summary, Action Taken, Timestamp

---

## 👩‍⚕️ Patient & Appointment Details
- CRUD for patient info
- Appointment booking with doctor availability check
- **New Feature**: On booking → system sends **email notification** with patient details

---

## ⚙️ Settings Page
- Doctor Management: Add/Edit/Remove doctors, timings
- AI Settings: Agent name, welcome message, language, instructions

---

## 📞 Voice Agent System
### Call Flow
1. Incoming call → AI picks up
2. Greeting → Configured welcome message
3. Intent detection → Appointment / Query / Spam
4. Execution:
   - Book appointment
   - Save transcript + summary
   - Push summary to dashboard
   - **Send email notification to doctor**

---

## 🏗️ Backend (.NET 8)
### Architecture
- ASP.NET Core Web API
- EF Core ORM (SQL Server/PostgreSQL)
- SignalR for real-time dashboard
- Twilio/Vonage for telephony
- Sarvam AI for STT + intent detection
- **Email Service**: SMTP / SendGrid / Outlook API

### Key Endpoints
- `/api/appointment/book`
  - Input: Patient + Doctor + Time
  - Process: Save appointment, push summary, send email
  - Output: Confirmation JSON

---

## 📧 Email Notification Flow
- Trigger: Appointment booked
- Recipient: Doctor’s registered email
- Content:

Subject: New Appointment Booking
Body:
Dear Dr. {DoctorName},

A new appointment has been booked.

Patient: {PatientName}
Age: {Age}
Contact: {Contact}
Appointment Time: {DateTime}

Regards,
Anuj’s AI Assistant

Code

- Implementation:
  - Configure SMTP/SendGrid in `appsettings.json`
  - Use `IEmailSender` service in ASP.NET Core
  - Call `EmailService.SendAsync()` after appointment booking

---

## ✅ Example Dashboard Grid Schema
| Caller Name   | Summary                          | Action Taken       | Timestamp           |
|---------------|----------------------------------|-------------------|---------------------|
| Rahul Sharma  | Asked for Dr. Mehta appointment | Booked for 5 PM   | 2026-09-04 20:30    |
| Unknown       | Spam call                       | Ended politely    | 2026-09-04 20:32    |

---

## 🔧 Tech Stack
- **Frontend**: React + TailwindCSS
- **Backend**: ASP.NET Core 8 Web API
- **Database**: SQL Server / PostgreSQL
- **Realtime**: SignalR
- **Telephony**: Twilio / Vonage
- **AI**: Sarvam AI (STT + LLM + TTS)
- **Email**: SMTP / SendGrid / Outlook API
- **Graphs**: Chart.js