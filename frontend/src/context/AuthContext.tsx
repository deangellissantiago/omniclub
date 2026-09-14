import { createContext, useContext, useMemo, useState, type ReactNode } from "react";
import { login as loginRequest, register as registerRequest } from "../api/auth";
import { clearToken, getToken, setToken } from "../api/client";

interface AuthInfo {
  adminName: string;
  tenantName: string;
}

interface AuthContextValue {
  isAuthenticated: boolean;
  auth: AuthInfo | null;
  login: (email: string, password: string) => Promise<void>;
  register: (tenantName: string, adminName: string, email: string, password: string) => Promise<void>;
  logout: () => void;
}

const AUTH_INFO_KEY = "checkin_auth_info";

const AuthContext = createContext<AuthContextValue | undefined>(undefined);

function readStoredAuth(): AuthInfo | null {
  const raw = localStorage.getItem(AUTH_INFO_KEY);
  return raw ? (JSON.parse(raw) as AuthInfo) : null;
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [auth, setAuth] = useState<AuthInfo | null>(() => (getToken() ? readStoredAuth() : null));

  function applyLoginResult(result: { token: string; adminName: string; tenantName: string }) {
    setToken(result.token);
    const info: AuthInfo = { adminName: result.adminName, tenantName: result.tenantName };
    localStorage.setItem(AUTH_INFO_KEY, JSON.stringify(info));
    setAuth(info);
  }

  const value = useMemo<AuthContextValue>(
    () => ({
      isAuthenticated: !!auth,
      auth,
      login: async (email: string, password: string) => {
        const result = await loginRequest({ email, password });
        applyLoginResult(result);
      },
      register: async (tenantName: string, adminName: string, email: string, password: string) => {
        const result = await registerRequest({ tenantName, adminName, email, password });
        applyLoginResult(result);
      },
      logout: () => {
        clearToken();
        localStorage.removeItem(AUTH_INFO_KEY);
        setAuth(null);
      },
    }),
    [auth]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth(): AuthContextValue {
  const ctx = useContext(AuthContext);
  if (!ctx) throw new Error("useAuth deve ser usado dentro de um AuthProvider");
  return ctx;
}
