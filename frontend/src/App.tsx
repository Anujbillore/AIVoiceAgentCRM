import type { ReactNode } from "react";
import { BrowserRouter, Navigate, Route, Routes } from "react-router-dom";
import { AuthProvider, useAuth } from "./context/AuthContext";
import { Layout } from "./components/Layout";
import { ProtectedRoute } from "./components/ProtectedRoute";
import { LoginPage } from "./pages/Login";
import { RegisterPage } from "./pages/Register";
import { ForgotPasswordPage } from "./pages/ForgotPassword";
import { ResetPasswordPage } from "./pages/ResetPassword";
import { DashboardPage } from "./pages/Dashboard";
import { PatientsPage } from "./pages/Patients";
import { ProfilePage } from "./pages/Profile";
import { AppointmentsPage } from "./pages/Appointments";
import { SettingsPage } from "./pages/Settings";
import { CallLogPage } from "./pages/CallLog";
import { SupportPage } from "./pages/Support";
import { NotificationsPage } from "./pages/Notifications";

function GuestOnly({ children }: { children: ReactNode }) {
  const { user } = useAuth();
  if (user) return <Navigate to={user.role === "Patient" ? "/appointments" : "/"} replace />;
  return <>{children}</>;
}

export default function App() {
  return (
    <AuthProvider>
      <BrowserRouter>
        <Routes>
          <Route
            path="/login"
            element={
              <GuestOnly>
                <LoginPage />
              </GuestOnly>
            }
          />
          <Route
            path="/register"
            element={
              <GuestOnly>
                <RegisterPage />
              </GuestOnly>
            }
          />
          <Route path="/forgot-password" element={<ForgotPasswordPage />} />
          <Route path="/reset-password" element={<ResetPasswordPage />} />
          <Route
            element={
              <ProtectedRoute>
                <Layout />
              </ProtectedRoute>
            }
          >
            <Route
              path="/"
              element={
                <ProtectedRoute roles={["Admin", "Doctor"]}>
                  <DashboardPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="/calls"
              element={
                <ProtectedRoute roles={["Admin", "Doctor"]}>
                  <CallLogPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="/patients"
              element={
                <ProtectedRoute roles={["Admin", "Doctor"]}>
                  <PatientsPage />
                </ProtectedRoute>
              }
            />
            <Route
              path="/profile"
              element={
                <ProtectedRoute roles={["Patient"]}>
                  <ProfilePage />
                </ProtectedRoute>
              }
            />
            <Route path="/appointments" element={<AppointmentsPage />} />
            <Route path="/support" element={<SupportPage />} />
            <Route
              path="/notifications"
              element={
                <ProtectedRoute roles={["Admin", "Doctor"]}>
                  <NotificationsPage />
                </ProtectedRoute>
              }
            />
            <Route path="/billing" element={<Navigate to="/" replace />} />
            <Route path="/insurance" element={<Navigate to="/" replace />} />
            <Route path="/payments" element={<Navigate to="/" replace />} />
            <Route path="/voice" element={<Navigate to="/" replace />} />
            <Route
              path="/settings"
              element={
                <ProtectedRoute roles={["Admin"]}>
                  <SettingsPage />
                </ProtectedRoute>
              }
            />
          </Route>
        </Routes>
      </BrowserRouter>
    </AuthProvider>
  );
}
