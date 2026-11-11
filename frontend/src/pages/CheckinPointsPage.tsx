import { useEffect, useState, type FormEvent } from "react";
import {
  connectTotalPass,
  createCheckinPoint,
  deleteCheckinPoint,
  listCheckinPoints,
  syncCheckinPointProducts,
  updateCheckinPoint,
} from "../api/checkinPoints";
import type { CheckinPoint } from "../api/types";
import { extractErrorMessage } from "../api/client";
import { ActiveBadge, AppBadge, TableEmpty } from "../components/ui";
import { centsToReaisInput, formatMoneyCents, reaisInputToCents } from "../lib/money";

/** Estado do formulário — preços como texto em reais (ex.: "1,50"), convertidos só no submit
 * pra não perder o que a pessoa está digitando (ex.: "1," no meio de digitar "1,50"). */
interface PointForm {
  name: string;
  active: boolean;
  wellhubEnabled: boolean;
  wellhubGymId: string;
  wellhubPrice: string;
  totalPassEnabled: boolean;
  totalPassKey: string;
  totalPassPrice: string;
}

const EMPTY_FORM: PointForm = {
  name: "",
  active: true,
  wellhubEnabled: true,
  wellhubGymId: "",
  wellhubPrice: "",
  totalPassEnabled: false,
  totalPassKey: "",
  totalPassPrice: "",
};

export function CheckinPointsPage() {
  const [points, setPoints] = useState<CheckinPoint[]>([]);
  const [form, setForm] = useState<PointForm>(EMPTY_FORM);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [busyId, setBusyId] = useState<string | null>(null);

  const editingPoint = points.find((p) => p.id === editingId);
  const keyHint = editingPoint?.totalPass?.placeApiKeyHint;

  async function refresh() {
    setPoints(await listCheckinPoints());
  }

  useEffect(() => {
    refresh().catch((err) => setError(extractErrorMessage(err)));
  }, []);

  function startEdit(point: CheckinPoint) {
    setEditingId(point.id);
    setError(null);
    setNotice(null);
    setForm({
      name: point.name,
      active: point.active,
      wellhubEnabled: point.apps.includes("Wellhub"),
      wellhubGymId: point.externalId,
      wellhubPrice: centsToReaisInput(point.pricePerCheckinCents),
      totalPassEnabled: point.apps.includes("TotalPass"),
      totalPassKey: "",
      totalPassPrice: centsToReaisInput(point.totalPass?.pricePerCheckinCents),
    });
  }

  function cancelEdit() {
    setEditingId(null);
    setForm(EMPTY_FORM);
  }

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    setNotice(null);

    if (!form.wellhubEnabled && !form.totalPassEnabled) {
      setError("Ative pelo menos um app: Wellhub e/ou TotalPass.");
      return;
    }
    const turningTotalPassOff = editingPoint?.apps.includes("TotalPass") && !form.totalPassEnabled;
    if (turningTotalPassOff && !confirm("Desativar o TotalPass nesta unidade? A chave e a conexão com o TotalPass serão apagadas e os check-ins de lá deixam de ser recebidos.")) {
      return;
    }

    setLoading(true);
    try {
      const payload = {
        name: form.name.trim(),
        active: form.active,
        externalId: form.wellhubEnabled ? form.wellhubGymId.trim() : "",
        pricePerCheckinCents: form.wellhubEnabled ? reaisInputToCents(form.wellhubPrice) : null,
        totalPassEnabled: form.totalPassEnabled,
        totalPassPlaceApiKey: form.totalPassEnabled && form.totalPassKey.trim() ? form.totalPassKey.trim() : null,
        totalPassPricePerCheckinCents: form.totalPassEnabled ? reaisInputToCents(form.totalPassPrice) : null,
      };
      const saved = editingId ? await updateCheckinPoint(editingId, payload) : await createCheckinPoint(payload);
      cancelEdit();
      await refresh();
      if (saved.totalPass && !saved.totalPass.connectedAt) {
        setNotice(`"${saved.name}" salva. Falta clicar em "Conectar ao TotalPass" na lista abaixo para começar a receber os check-ins do TotalPass.`);
      }
    } catch (err) {
      setError(extractErrorMessage(err));
    } finally {
      setLoading(false);
    }
  }

  async function handleDelete(id: string) {
    if (!confirm("Remover esta unidade?")) return;
    try {
      await deleteCheckinPoint(id);
      await refresh();
    } catch (err) {
      setError(extractErrorMessage(err));
    }
  }

  async function runRowAction(id: string, action: () => Promise<void>) {
    setError(null);
    setNotice(null);
    setBusyId(id);
    try {
      await action();
    } catch (err) {
      setError(extractErrorMessage(err));
    } finally {
      setBusyId(null);
    }
  }

  function handleConnectTotalPass(point: CheckinPoint) {
    runRowAction(point.id, async () => {
      const updated = await connectTotalPass(point.id);
      await refresh();
      setNotice(`"${updated.name}" conectada ao TotalPass${updated.totalPass?.placeName ? ` (unidade ${updated.totalPass.placeName})` : ""}. Os check-ins passam a chegar automaticamente.`);
    });
  }

  function handleSyncProducts(point: CheckinPoint) {
    runRowAction(point.id, async () => {
      await syncCheckinPointProducts(point.id);
      await refresh();
    });
  }

  return (
    <div>
      <div className="page-header">
        <h1>Pontos de check-in</h1>
        <p className="muted">
          Cadastre as unidades do clube e escolha por quais aplicativos cada uma recebe check-ins — Wellhub, TotalPass ou
          os dois.
        </p>
      </div>

      <section className="panel">
        <h2>{editingId ? "Editar unidade" : "Nova unidade"}</h2>
        <form className="form-grid" onSubmit={handleSubmit}>
          <label>
            Nome *
            <input
              value={form.name}
              onChange={(e) => setForm({ ...form, name: e.target.value })}
              placeholder="Ex.: Dynamis Santa Lúcia"
              required
            />
          </label>
          <label className="checkbox-label">
            <input type="checkbox" checked={form.active} onChange={(e) => setForm({ ...form, active: e.target.checked })} />
            Ativa
          </label>

          <div className={`integration-block${form.wellhubEnabled ? " on" : ""}`}>
            <label className="checkbox-label integration-toggle">
              <input
                type="checkbox"
                checked={form.wellhubEnabled}
                onChange={(e) => setForm({ ...form, wellhubEnabled: e.target.checked })}
              />
              <AppBadge app="Wellhub" /> Aceita check-ins do Wellhub
            </label>
            {form.wellhubEnabled && (
              <>
                <label>
                  Gym ID no Wellhub *
                  <input
                    value={form.wellhubGymId}
                    onChange={(e) => setForm({ ...form, wellhubGymId: e.target.value })}
                    placeholder="Ex.: 500977"
                    required
                  />
                </label>
                <label>
                  Repasse por check-in (R$)
                  <input
                    inputMode="decimal"
                    value={form.wellhubPrice}
                    onChange={(e) => setForm({ ...form, wellhubPrice: e.target.value })}
                    placeholder="Ex.: 15,00"
                  />
                </label>
              </>
            )}
          </div>

          <div className={`integration-block${form.totalPassEnabled ? " on" : ""}`}>
            <label className="checkbox-label integration-toggle">
              <input
                type="checkbox"
                checked={form.totalPassEnabled}
                onChange={(e) => setForm({ ...form, totalPassEnabled: e.target.checked })}
              />
              <AppBadge app="TotalPass" /> Aceita check-ins do TotalPass
            </label>
            {form.totalPassEnabled && (
              <>
                <label>
                  Chave da unidade no TotalPass (place_api_key){keyHint ? "" : " *"}
                  <input
                    value={form.totalPassKey}
                    onChange={(e) => setForm({ ...form, totalPassKey: e.target.value })}
                    placeholder={keyHint ? `Manter a atual (${keyHint})` : "Gerada pela academia no painel do TotalPass"}
                    required={!keyHint}
                    autoComplete="off"
                  />
                </label>
                <label>
                  Repasse por check-in (R$)
                  <input
                    inputMode="decimal"
                    value={form.totalPassPrice}
                    onChange={(e) => setForm({ ...form, totalPassPrice: e.target.value })}
                    placeholder="Ex.: 20,00"
                  />
                </label>
                <p className="muted integration-hint">
                  Depois de salvar, clique em <b>Conectar ao TotalPass</b> na lista: o OmniClub valida a chave e passa a
                  receber os check-ins desta unidade.
                </p>
              </>
            )}
          </div>

          <div className="form-actions">
            <button type="submit" className="btn-primary" disabled={loading}>
              {editingId ? "Salvar alterações" : "Cadastrar unidade"}
            </button>
            {editingId && (
              <button type="button" className="btn-ghost" onClick={cancelEdit}>Cancelar</button>
            )}
          </div>
        </form>
        {error && <p className="error-text">{error}</p>}
        {notice && <p className="notice-text" style={{ margin: "0.9rem 0 0" }}>{notice}</p>}
      </section>

      <section className="panel panel-table">
        <h2>Unidades cadastradas</h2>
        <table className="data-table">
          <thead>
            <tr>
              <th>Unidade</th>
              <th>Apps</th>
              <th>Wellhub</th>
              <th>TotalPass</th>
              <th>Status</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {points.length === 0 && (
              <TableEmpty
                colSpan={6}
                title="Nenhuma unidade cadastrada ainda"
                hint="Cadastre as quadras/unidades do seu clube no formulário acima."
              />
            )}
            {points.map((p) => (
              <tr key={p.id} className={p.id === editingId ? "row-selected" : undefined}>
                <td className="cell-strong">{p.name}</td>
                <td>
                  <span className="app-badges">
                    {p.apps.map((a) => <AppBadge key={a} app={a} />)}
                  </span>
                </td>
                <td>
                  {p.apps.includes("Wellhub") ? (
                    <div className="cell-stack">
                      <span>Gym ID <b>{p.externalId}</b> · {formatMoneyCents(p.pricePerCheckinCents)}</span>
                      <div className="product-chips">
                        {p.products.map((prod) => (
                          <span key={prod.productId} className="badge badge-app-wellhub" title={`product_id ${prod.productId}`}>
                            {prod.name}{prod.virtual ? " (virtual)" : ""}
                          </span>
                        ))}
                        <button type="button" className="btn-link" disabled={busyId === p.id} onClick={() => handleSyncProducts(p)}>
                          {busyId === p.id ? "Buscando..." : p.products.length ? "Atualizar produtos" : "Buscar produtos"}
                        </button>
                      </div>
                    </div>
                  ) : (
                    <span className="muted">—</span>
                  )}
                </td>
                <td>
                  {p.totalPass ? (
                    <div className="cell-stack">
                      {p.totalPass.connectedAt ? (
                        <span className="badge badge-approved" title={`Conectado em ${new Date(p.totalPass.connectedAt).toLocaleString("pt-BR")}`}>
                          <span className="dot" />
                          Conectado{p.totalPass.placeName ? ` · ${p.totalPass.placeName}` : ""}
                        </span>
                      ) : (
                        <span className="badge badge-pending"><span className="dot" />Não conectado</span>
                      )}
                      <span className="muted">{formatMoneyCents(p.totalPass.pricePerCheckinCents)} por check-in</span>
                      <button type="button" className="btn-link" disabled={busyId === p.id} onClick={() => handleConnectTotalPass(p)}>
                        {busyId === p.id ? "Conectando..." : p.totalPass.connectedAt ? "Reconectar" : "Conectar ao TotalPass"}
                      </button>
                    </div>
                  ) : (
                    <span className="muted">—</span>
                  )}
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
