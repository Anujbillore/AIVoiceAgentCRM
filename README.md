# AI Voice Agent Web Portal

End-to-end clinic portal from `AI Voice Agent Web Portal.md`: React + Tailwind frontend, ASP.NET Core API, Identity roles, SignalR live call summaries, appointment booking with doctor availability, email notifications, and a Sarvam AI voice-agent flow.

## Supabase database

The API uses **Supabase PostgreSQL** (EF Core + Npgsql). Tables, Identity users, appointments, call logs, and emails are created on first startup.

1. In Supabase: **Project Settings → Database**.
2. Copy the **URI** (Session pooler on port `5432` is preferred for migrations).
3. Copy the example file and paste the URI:

```powershell
copy backend\appsettings.Local.json.example backend\appsettings.Local.json
```

`appsettings.Local.json` is gitignored. Use the **Session pooler** (IPv4). Do not use `db.*.supabase.co` on Render — that host is IPv6-only and the deploy will crash with "Network is unreachable".

```
postgresql://postgres.YOUR_PROJECT_REF:YOUR_PASSWORD@aws-0-YOUR-REGION.pooler.supabase.com:5432/postgres
```

Avoid the transaction pooler on port `6543` for `dotnet ef` / first migrate.

## Run locally

Use two terminals.

**API** (http://localhost:5147)

```powershell
cd backend
dotnet run --launch-profile http
```

**Web app** (http://localhost:5173)

```powershell
cd frontend
npm install
npm run dev
```

Open http://localhost:5173 and sign in.

## Free one-place host (Render)

The Docker image serves the React app and the API together. Create one **Web Service** on the [Render free plan](https://render.com).

1. Push this repo to GitHub.
2. Render → **New → Web Service** → connect the repo. Runtime: **Docker**. Instance: **Free**.
3. Set environment variables (do not put these in git):

| Variable | Value |
|---|---|
| `ConnectionStrings__DefaultConnection` | Supabase **Session pooler** URI. Username must be `postgres.YOUR_PROJECT_REF`, host `*.pooler.supabase.com`, port `5432`. Not `db.*.supabase.co`. |
| `Jwt__Key` | A long random string (32+ characters) |
| `Sarvam__ApiKey` | Optional for UI testing; required for phone STT/TTS |
| `Exotel__ApiKey` / `Exotel__ApiToken` | Optional until you test a real ExoPhone |

4. Deploy. Open `https://YOUR-SERVICE.onrender.com` and sign in with the demo admin account.
5. For an Exotel test, set the ExoML URL to `https://YOUR-SERVICE.onrender.com/api/voice/exotel/incoming`.

Free Render **sleeps after about 15 minutes**. The first visit can take a minute to wake. Open the site first before you dial the ExoPhone, or the webhook will time out. Local `ngrok` is still the most reliable free phone test.

| Role    | Email              | Password    |
|---------|--------------------|-------------|
| Admin   | admin@clinic.com   | Admin@123   |
| Doctor  | mehta@clinic.com   | Doctor@123  |
| Patient | patient@clinic.com | Patient@123 |

## What is included

- Login, patient registration, forgot/reset password (ASP.NET Identity)
- Dashboard with Chart.js graphs, live action-items grid (SignalR), and doctor email outbox
- Patient CRUD, patient self-service profile, and appointment booking with doctor timing checks
- Email to the doctor on booking (SMTP if configured, otherwise stored in the outbox)
- Settings: doctor management + AI agent name, greeting, language, instructions
- Voice assistant: live multi-turn call in the portal, Sarvam STT/TTS when configured, fallback intent, booking + doctor email, dashboard SignalR
- Twilio voice webhooks: `/api/voice/twilio/incoming` and `/api/voice/twilio/gather`
- Exotel ExoML webhooks: `/api/voice/exotel/incoming`, `/api/voice/exotel/turn`, `/api/voice/exotel/audio/{sessionId}`

## Voice assistant

1. Settings → AI agent: greeting, languages, voice, instructions.
2. Set `Sarvam:ApiKey` in `appsettings.Local.json` for speech-to-text and spoken replies (required for real phone speech).
3. Open **Voice Agent**, start a live call, and talk or type. The agent greets, detects appointment / query / spam, books if it has a doctor and time, and pushes the summary to the dashboard.
4. **Exotel + Sarvam (India production):** add `Sarvam:ApiKey`, `Exotel:ApiKey` / `ApiToken`, and `Voice:PublicBaseUrl` (ngrok https). In App Bazaar create an ExoML/URL app, paste `https://YOUR-NGROK/api/voice/exotel/incoming`, assign it to the ExoPhone.
5. **Twilio (optional):** set the voice URL to `https://YOUR-PUBLIC-HOST/api/voice/twilio/incoming` (POST).

## Optional configuration

In `backend/appsettings.json`:

- `Sarvam:ApiKey` — STT, chat intent, and TTS. Without a key, intent uses a local fallback so the demo still runs.
- `Exotel:ApiKey` / `Exotel:ApiToken` — Basic auth when downloading private Exotel recordings.
- `Email:SmtpHost` / username / password — doctor booking emails and password reset mail.

Exotel should use `/api/voice/exotel/incoming` (ExoML). Twilio can POST to `/api/voice/twilio/incoming`.
