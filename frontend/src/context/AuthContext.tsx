import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import { api, TOKEN_KEY, type AuthUser } from "../api/client";

interface AuthState {
  user: AuthUser | null;
  login: (email: string, password: string) => Promise<AuthUser>;
  acceptAuth: (data: AuthUser) => void;
  logout: () => void;
}

const AuthContext = createContext<AuthState | undefined>(undefined);

function readUser(): AuthUser | null {
  const raw = localStorage.getItem("clinic.user");
  const token = localStorage.getItem(TOKEN_KEY);
  if (!raw || !token) return null;
  try {
    const parsed = { ...JSON.parse(raw), token } as AuthUser;
    if (parsed.expiresAt && new Date(parsed.expiresAt).getTime() <= Date.now()) {
      localStorage.removeItem(TOKEN_KEY);
      localStorage.removeItem("clinic.user");
      return null;
    }
    return parsed;
  } catch {
    return null;
  }
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<AuthUser | null>(readUser);

  const value = useMemo<AuthState>(
    () => ({
      user,
      acceptAuth(data) {
        localStorage.setItem(TOKEN_KEY, data.token);
        localStorage.setItem("clinic.user", JSON.stringify({ ...data, token: undefined }));
        setUser(data);
      },
      async login(email, password) {
        const { data } = await api.post<AuthUser>("/auth/login", { email, password });
        localStorage.setItem(TOKEN_KEY, data.token);
        localStorage.setItem("clinic.user", JSON.stringify({ ...data, token: undefined }));
        setUser(data);
        return data;
      },
      logout() {
        localStorage.removeItem(TOKEN_KEY);
        localStorage.removeItem("clinic.user");
        setUser(null);
      },
    }),
    [user],
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth must be used within AuthProvider");
  return ctx;
}
