import axios from "axios";

const baseURL = import.meta.env.VITE_API_URL ?? "http://localhost:5080/api";

export const apiClient = axios.create({ baseURL });

const TOKEN_KEY = "checkin_token";

export function getToken(): string | null {
  return localStorage.getItem(TOKEN_KEY);
}

export function setToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token);
}

export function clearToken(): void {
  localStorage.removeItem(TOKEN_KEY);
}

apiClient.interceptors.request.use((config) => {
  const token = getToken();
  if (token) {
    config.headers.Authorization = `Bearer ${token}`;
  }
  return config;
});

apiClient.interceptors.response.use(
  (response) => response,
  (error) => {
    if (error.response?.status === 401) {
      clearToken();
      if (window.location.pathname !== "/login") {
        window.location.href = "/login";
      }
    }
    // Assinatura inativa (SubscriptionGateMiddleware): continua logado, só manda pra tela de
    // assinatura em vez de derrubar a sessão como no 401.
    if (error.response?.status === 402 && window.location.pathname !== "/assinatura") {
      window.location.href = "/assinatura";
    }
    return Promise.reject(error);
  }
);

export function extractErrorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    return error.response?.data?.error ?? error.message;
  }
  return "Ocorreu um erro inesperado.";
}
