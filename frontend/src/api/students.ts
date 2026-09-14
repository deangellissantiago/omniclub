import { apiClient } from "./client";
import type { Student, UpsertStudent } from "./types";

export async function listStudents(): Promise<Student[]> {
  const { data } = await apiClient.get<Student[]>("/students");
  return data;
}

export async function createStudent(payload: UpsertStudent): Promise<Student> {
  const { data } = await apiClient.post<Student>("/students", payload);
  return data;
}

export async function updateStudent(id: string, payload: UpsertStudent): Promise<Student> {
  const { data } = await apiClient.put<Student>(`/students/${id}`, payload);
  return data;
}

export async function deleteStudent(id: string): Promise<void> {
  await apiClient.delete(`/students/${id}`);
}
