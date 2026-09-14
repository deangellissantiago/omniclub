import { useEffect, useState, type FormEvent } from "react";
import { createStudent, deleteStudent, listStudents, updateStudent } from "../api/students";
import type { Student, UpsertStudent } from "../api/types";
import { extractErrorMessage } from "../api/client";
import { ActiveBadge, Avatar, TableEmpty } from "../components/ui";

const EMPTY_FORM: UpsertStudent = {
  name: "",
  email: "",
  phone: "",
  document: "",
  wellhubMemberId: "",
  totalPassMemberId: "",
  active: true,
};

export function StudentsPage() {
  const [students, setStudents] = useState<Student[]>([]);
  const [form, setForm] = useState<UpsertStudent>(EMPTY_FORM);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function refresh() {
    setStudents(await listStudents());
  }

  useEffect(() => {
    refresh().catch((err) => setError(extractErrorMessage(err)));
  }, []);

  function startEdit(student: Student) {
    setEditingId(student.id);
    setForm({
      name: student.name,
      email: student.email ?? "",
      phone: student.phone ?? "",
      document: student.document ?? "",
      wellhubMemberId: student.wellhubMemberId ?? "",
      totalPassMemberId: student.totalPassMemberId ?? "",
      active: student.active,
    });
  }

  function cancelEdit() {
    setEditingId(null);
    setForm(EMPTY_FORM);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      if (editingId) {
        await updateStudent(editingId, form);
      } else {
        await createStudent(form);
      }
      cancelEdit();
      await refresh();
    } catch (err) {
      setError(extractErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }

  async function handleDelete(id: string) {
    if (!confirm("Remover este aluno?")) return;
    try {
      await deleteStudent(id);
      await refresh();
    } catch (err) {
      setError(extractErrorMessage(err));
    }
  }

  return (
    <div>
      <div className="page-header">
        <h1>Alunos</h1>
        <p className="muted">Cadastre os alunos e vincule o ID do aplicativo de benefício para o casamento automático dos check-ins.</p>
      </div>

      <section className="panel">
        <h2>{editingId ? "Editar aluno" : "Novo aluno"}</h2>
        <form className="form-grid" onSubmit={handleSubmit}>
          <label>
            Nome *
            <input value={form.name} onChange={(e) => setForm({ ...form, name: e.target.value })} required />
          </label>
          <label>
            E-mail
            <input type="email" value={form.email ?? ""} onChange={(e) => setForm({ ...form, email: e.target.value })} />
          </label>
          <label>
            Telefone
            <input value={form.phone ?? ""} onChange={(e) => setForm({ ...form, phone: e.target.value })} />
          </label>
          <label>
            CPF
            <input value={form.document ?? ""} onChange={(e) => setForm({ ...form, document: e.target.value })} />
          </label>
          <label>
            Wellhub ID
            <input
              value={form.wellhubMemberId ?? ""}
              onChange={(e) => setForm({ ...form, wellhubMemberId: e.target.value })}
              placeholder="Identificador único do aluno no Wellhub"
            />
          </label>
          <label>
            TotalPass ID <span className="muted">(em breve)</span>
            <input
              value={form.totalPassMemberId ?? ""}
              onChange={(e) => setForm({ ...form, totalPassMemberId: e.target.value })}
              disabled
            />
          </label>
          <label className="checkbox-label">
            <input type="checkbox" checked={form.active} onChange={(e) => setForm({ ...form, active: e.target.checked })} />
            Ativo
          </label>

          <div className="form-actions">
            <button type="submit" className="btn-primary" disabled={loading}>
              {editingId ? "Salvar alterações" : "Cadastrar aluno"}
            </button>
            {editingId && (
              <button type="button" className="btn-ghost" onClick={cancelEdit}>Cancelar</button>
            )}
          </div>
        </form>
        {error && <p className="error-text">{error}</p>}
      </section>

      <section className="panel panel-table">
        <h2>Alunos cadastrados</h2>
        <table className="data-table">
          <thead>
            <tr>
              <th>Nome</th>
              <th>E-mail</th>
              <th>Telefone</th>
              <th>Wellhub ID</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {students.length === 0 && (
              <TableEmpty
                colSpan={6}
                title="Nenhum aluno cadastrado ainda"
                hint="Use o formulário acima para cadastrar o primeiro aluno."
              />
            )}
            {students.map((s) => (
              <tr key={s.id}>
                <td>
                  <span className="cell-person">
                    <Avatar name={s.name} small />
                    <span className="cell-strong">{s.name}</span>
                  </span>
                </td>
                <td>{s.email ?? "-"}</td>
                <td>{s.phone ?? "-"}</td>
                <td>{s.wellhubMemberId ?? "-"}</td>
                <td><ActiveBadge active={s.active} /></td>
                <td className="actions-cell">
                  <button className="btn-link" onClick={() => startEdit(s)}>Editar</button>
                  <button className="btn-link danger" onClick={() => handleDelete(s.id)}>Remover</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
