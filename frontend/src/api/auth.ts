import { apiClient } from "./client";
import type { LoginRequest, LoginResponse, RegisterRequest } from "./types";

export async function login(request: LoginRequest): Promise<LoginResponse> {
  const { data } = await apiClient.post<LoginResponse>("/auth/login", request);
  return data;
}

export async function register(request: RegisterRequest): Promise<LoginResponse> {
  const { data } = await apiClient.post<LoginResponse>("/auth/register", request);
  return data;
}
