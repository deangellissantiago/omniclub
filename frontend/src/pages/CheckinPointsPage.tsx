import { useEffect, useState, type FormEvent } from "react";
import {
  createCheckinPoint,
  deleteCheckinPoint,
  listCheckinPoints,
  syncCheckinPointProducts,
  updateCheckinPoint,
} from "../api/checkinPoints";
import type { CheckinPoint, IntegrationApp, UpsertCheckinPoint } from "../api/types";
import { extractErrorMessage } from "../api/client";
import { ActiveBadge, AppBadge, TableEmpty } from "../components/ui";
import { centsToReaisInput, formatMoneyCents, reaisInputToCents } from "../lib/money";

const EMPTY_FORM: UpsertCheckinPoint = {
  app: "Wellhub",
  externalId: "",
  name: "",
  active: true,
  pricePerCheckinCents: null,
};

export function CheckinPointsPage() {
  const [points, setPoints] = useState<CheckinPoint[]>([]);
  const [form, setForm] = useState<UpsertCheckinPoint>(EMPTY_FORM);
  // Texto do input de preço em reais (ex.: "1,50") — convertido pra centavos só no submit,
  // pra não perder o que a pessoa está digitando (ex.: "1," no meio de digitar "1,50").
  const [priceText, setPriceText] = useState("");
  const [editingId, setEditingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [syncingId, setSyncingId] = useState<string | null>(null);

  async function refresh() {
    setPoints(await listCheckinPoints());
  }

  useEffect(() => {
    refresh().catch((err) => setError(extractErrorMessage(err)));
  }, []);

  function startEdit(point: CheckinPoint) {
    setEditingId(point.id);
    setForm({ app: point.app, externalId: point.externalId, name: point.name, active: point.active });
    setPriceText(centsToReaisInput(point.pricePerCheckinCents));
  }

  function cancelEdit() {
    setEditingId(null);
    setForm(EMPTY_FORM);
    setPriceText("");
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setLoading(true);
    try {
      const payload = { ...form, pricePerCheckinCents: reaisInputToCents(priceText) };
      if (editingId) {
        await updateCheckinPoint(editingId, payload);
      } else {
        await createCheckinPoint(payload);
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
    if (!confirm("Remover este ponto de check-in?")) return;
    try {
      await deleteCheckinPoint(id);
      await refresh();
    } catch (err) {
      setError(extractErrorMessage(err));
    }
  }

  async function handleSyncProducts(id: string) {
    setError(null);
    setSyncingId(id);
    try {
      await syncCheckinPointProducts(id);
      await refresh();
    } catch (err) {
      setError(extractErrorMessage(err));
    } finally {
      setSyncingId(null);
    }
  }

  return (
    <div>
      <div className="page-header">
        <h1>Pontos de check-in</h1>
        <p className="muted">
          Cadastre as quadras e unidades onde os alunos podem fazer check-in pelos aplicativos parceiros.
        </p>
      </div>

      <section className="panel">
        <h2>{editingId ? "Editar ponto de check-in" : "Novo ponto de check-in"}</h2>
        <form className="form-grid" onSubmit={handleSubmit}>
          <label>
            Aplicativo *
            <select value={form.app} onChange={(e) => setForm({ ...form, app: e.target.value as IntegrationApp })}>
              <option value="Wellhub">Wellhub</option>
              <option value="TotalPass" disabled>TotalPass (em breve)</option>
            </select>
          </label>
          <label>
            Identificador no app *
            <input
              value={form.externalId}
              onChange={(e) => setForm({ ...form, externalId: e.target.value })}
              placeholder="Ex.: 500977"
              required
            />
          </label>
          <label>
            Nome *
            <input
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              placeholder="Ex.: Dynamis Santa Lúcia"
              required
            />
          </label>
          <label>
            Valor por check-in (R$)
            <input
              inputMode="decimal"
              value={priceText}
              onChange={(e) => setPriceText(e.target.value)}
              placeholder="Ex.: 1,50"
            />
          </label>
          <label className="checkbox-label">
            <input type="checkbox" checked={form.active} onChange={(e) => setForm({ ...form, active: e.target.checked })} />
            Ativo
          </label>

          <div className="form-actions">
            <button type="submit" className="btn-primary" disabled={loading}>
              {editingId ? "Salvar alterações" : "Cadastrar ponto"}
            </button>
            {editingId && (
              <button type="button" className="btn-ghost" onClick={cancelEdit}>Cancelar</button>
            )}
          </div>
        </form>
        {error && <p className="error-text">{error}</p>}
      </section>

      <section className="panel panel-table">
        <h2>Pontos cadastrados</h2>
        <table className="data-table">
          <thead>
            <tr>
              <th>Nome</th>
              <th>App</th>
              <th>Identificador</th>
              <th>Valor/check-in</th>
              <th>Produtos</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {points.length === 0 && (
              <TableEmpty
                colSpan={7}
                title="Nenhum ponto de check-in cadastrado ainda"
                hint="Cadastre as quadras/unidades do seu clube no formulário acima."
              />
            )}
            {points.map((p) => (
              <tr key={p.id}>
                <td className="cell-strong">{p.name}</td>
                <td><AppBadge app={p.app} /></td>
                <td>{p.externalId}</td>
                <td className={p.pricePerCheckinCents == null ? "muted" : undefined}>{formatMoneyCents(p.pricePerCheckinCents)}</td>
                <td>
                  <div className="product-chips">
                    {p.products.length === 0 ? (
                      <span className="muted">Nenhum produto vinculado</span>
                    ) : (
                      p.products.map((prod) => (
                        <span key={prod.productId} className="badge badge-app-wellhub" title={`product_id ${prod.productId}`}>
                          {prod.name}{prod.virtual ? " (virtual)" : ""}
                        </span>
                      ))
                    )}
                    {p.app === "Wellhub" && (
                      <button
                        type="button"
                        className="btn-link"
                        disabled={syncingId === p.id}
                        onClick={() => handleSyncProducts(p.id)}
                      >
                        {syncingId === p.id ? "Buscando..." : "Buscar produtos"}
                      </button>
                    )}
                  </div>
                </td>
                <td><ActiveBadge active={p.active} /></td>
                <td className="actions-cell">
                  <button className="btn-link" onClick={() => startEdit(p)}>Editar</button>
                  <button className="btn-link danger" onClick={() => handleDelete(p.id)}>Remover</button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>
    </div>
  );
}
