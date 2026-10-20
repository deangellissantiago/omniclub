import { useEffect, useState, type FormEvent } from "react";
import {
  getWebhookSettings,
  listWebhookDeliveries,
  regenerateWebhookSecret,
  retryWebhookDeliveries,
  retryWebhookDelivery,
  sendWebhookTest,
  updateWebhookSettings,
} from "../api/webhook";
import type { OutboundWebhookDelivery, OutboundWebhookSettings, OutboundWebhookStatus } from "../api/types";
import { extractErrorMessage } from "../api/client";
import { TableEmpty } from "../components/ui";

const STATUS: Record<OutboundWebhookStatus, { label: string; tone: string }> = {
  Delivered: { label: "Entregue", tone: "approved" },
  Pending: { label: "Aguardando reenvio", tone: "pending" },
  Failed: { label: "Falhou", tone: "rejected" },
};

const FILTERS: { value: OutboundWebhookStatus | ""; label: string }[] = [
  { value: "", label: "Todos" },
  { value: "Failed", label: "Falharam" },
  { value: "Pending", label: "Aguardando" },
  { value: "Delivered", label: "Entregues" },
];

const EXAMPLE_PAYLOAD = `{
  "id": "3f6c1e2a-…",
  "tipo": "checkin.aprovado",
  "atletaEmail": "ana@mail.com",
  "ocorridoEm": "2026-10-16T18:00:00-03:00",
  "origem": "Wellhub",
  "valorRepasse": 25.00,
  "wellhubId": "1000000000001",
  "atletaNome": "Ana Souza",
  "atletaTelefone": "+5531988887777",
  "unidade": { "gymId": "547050", "nome": "Dynamis Vila da Serra" }
}`;

function formatDateTime(value?: string | null): string {
  return value ? new Date(value).toLocaleString("pt-BR") : "-";
}

export function WebhookPage() {
  const [settings, setSettings] = useState<OutboundWebhookSettings | null>(null);
  const [url, setUrl] = useState("");
  const [showSecret, setShowSecret] = useState(false);
  const [deliveries, setDeliveries] = useState<OutboundWebhookDelivery[]>([]);
  const [filter, setFilter] = useState<OutboundWebhookStatus | "">("");
  const [selected, setSelected] = useState<Set<string>>(new Set());
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  function applySettings(s: OutboundWebhookSettings) {
    setSettings(s);
    setUrl(s.url ?? "");
  }

  async function refreshDeliveries(status: OutboundWebhookStatus | "" = filter) {
    const list = await listWebhookDeliveries(status || undefined);
    setDeliveries(list);
    // Mantém selecionado só o que continua visível na lista.
    const visible = new Set(list.map((d) => d.id));
    setSelected((prev) => new Set([...prev].filter((id) => visible.has(id))));
  }

  useEffect(() => {
    getWebhookSettings().then(applySettings).catch((err) => setError(extractErrorMessage(err)));
  }, []);

  useEffect(() => {
    listWebhookDeliveries(filter || undefined)
      .then((list) => {
        setDeliveries(list);
        setSelected(new Set());
      })
      .catch((err) => setError(extractErrorMessage(err)));
  }, [filter]);

  const allSelected = deliveries.length > 0 && deliveries.every((d) => selected.has(d.id));

  function toggle(id: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(id)) next.delete(id);
      else next.add(id);
      return next;
    });
  }

  function toggleAll() {
    setSelected(allSelected ? new Set() : new Set(deliveries.map((d) => d.id)));
  }

  async function run(action: () => Promise<void>) {
    setError(null);
    setNotice(null);
    setBusy(true);
    try {
      await action();
    } catch (err) {
      setError(extractErrorMessage(err));
    } finally {
      setBusy(false);
    }
  }

  function handleSave(e: FormEvent) {
    e.preventDefault();
    run(async () => {
      applySettings(await updateWebhookSettings({ url: url.trim() || null }));
      setNotice(url.trim() ? "Webhook salvo. Cada check-in aprovado será enviado para essa URL." : "Webhook desativado.");
    });
  }

  function handleDisable() {
    if (!confirm("Desativar o envio? Check-ins aprovados deixam de ser enviados ao seu sistema.")) return;
    run(async () => {
      applySettings(await updateWebhookSettings({ url: null }));
      setNotice("Webhook desativado.");
    });
  }

  function handleRegenerate() {
    if (!confirm("Gerar um novo segredo? O sistema que recebe precisa ser atualizado com o novo valor, senão vai recusar os envios.")) return;
    run(async () => {
      applySettings(await regenerateWebhookSecret());
      setShowSecret(true);
      setNotice("Novo segredo gerado. Copie e atualize no sistema que recebe.");
    });
  }

  function handleCopy() {
    if (!settings?.secret) return;
    navigator.clipboard.writeText(settings.secret).then(
      () => setNotice("Segredo copiado."),
      () => setError("Não foi possível copiar — selecione o texto e copie manualmente."),
    );
  }

  function handleTest() {
    run(async () => {
      const result = await sendWebhookTest();
      if (result.success) {
        setNotice(`Evento de teste entregue (HTTP ${result.statusCode}). A URL e a assinatura estão funcionando.`);
      } else {
        setError(`O evento de teste não foi aceito: ${result.error ?? "erro desconhecido"}`);
      }
    });
  }

  function handleRetry(id: string) {
    run(async () => {
      await retryWebhookDelivery(id);
      await refreshDeliveries();
      setNotice("Reenvio agendado — sai em alguns segundos. Clique em Atualizar para ver o resultado.");
    });
  }

  function handleRetrySelected() {
    const ids = [...selected];
    if (ids.length === 0) return;
    const delivered = deliveries.filter((d) => selected.has(d.id) && d.status === "Delivered").length;
    const message =
      `Reenviar ${ids.length} envio(s) selecionado(s)?` +
      (delivered > 0
        ? `\n\n${delivered} já tinha(m) sido entregue(s). Eles vão de novo com o mesmo id — o sistema que recebe pode tratá-los como duplicados e ignorar.`
        : "");
    if (!confirm(message)) return;

    run(async () => {
      const requeued = await retryWebhookDeliveries(ids);
      setSelected(new Set());
      await refreshDeliveries();
      setNotice(`${requeued} envio(s) voltaram para a fila e saem em alguns segundos. Clique em Atualizar para ver o resultado.`);
    });
  }

  return (
    <div>
      <div className="page-header">
        <h1>Webhook</h1>
        <p className="muted">
          Envie cada check-in aprovado para o sistema do seu clube (presença, crédito, pontos) em tempo real, com
          assinatura de segurança e reenvio automático em caso de falha.
        </p>
      </div>

      {error && <p className="error-text" style={{ marginTop: 0, marginBottom: "1.2rem" }}>{error}</p>}
      {notice && <p className="notice-text">{notice}</p>}

      <section className="panel">
        <div className="panel-toolbar" style={{ padding: 0, marginBottom: "1rem" }}>
          <h2>Configuração</h2>
          {settings && (
            <span className={`badge ${settings.enabled ? "badge-approved" : "badge-pending"}`}>
              <span className="dot" />
              {settings.enabled ? "Ativo" : "Desativado"}
            </span>
          )}
        </div>
        <form className="form-grid" onSubmit={handleSave}>
          <label style={{ gridColumn: "1 / -1" }}>
            URL que recebe os eventos
            <input
              type="url"
              value={url}
              onChange={(e) => setUrl(e.target.value)}
              placeholder="https://seu-sistema.com.br/api/omniclub/webhook"
            />
          </label>
          {settings?.secret && (
            <label style={{ gridColumn: "1 / -1" }}>
              Segredo (assinatura HMAC-SHA256)
              <span className="secret-row">
                <input readOnly value={showSecret ? settings.secret : "•".repeat(32)} onFocus={(e) => e.target.select()} />
                <button type="button" className="btn-ghost" onClick={() => setShowSecret((v) => !v)}>{showSecret ? "Ocultar" : "Mostrar"}</button>
                <button type="button" className="btn-ghost" onClick={handleCopy}>Copiar</button>
                <button type="button" className="btn-ghost" disabled={busy} onClick={handleRegenerate}>Gerar novo</button>
              </span>
            </label>
          )}
          <div className="form-actions">
            <button type="submit" className="btn-primary" disabled={busy}>Salvar</button>
            {settings?.enabled && (
              <>
                <button type="button" className="btn-ghost" disabled={busy} onClick={handleTest}>Enviar evento de teste</button>
                <button type="button" className="btn-link danger" disabled={busy} onClick={handleDisable}>Desativar</button>
              </>
            )}
          </div>
        </form>
      </section>

      <section className="panel panel-table">
        <div className="panel-toolbar">
          <h2>Envios recentes</h2>
          <div className="toolbar-actions">
            <button className="btn-primary" disabled={busy || selected.size === 0} onClick={handleRetrySelected}>
              Reenviar selecionados{selected.size > 0 ? ` (${selected.size})` : ""}
            </button>
            <button className="btn-ghost" disabled={busy} onClick={() => run(() => refreshDeliveries())}>Atualizar</button>
          </div>
        </div>
        <div className="tabs" style={{ margin: "0 1.5rem 0.75rem" }}>
          {FILTERS.map((f) => (
            <button key={f.value || "all"} className={`tab${filter === f.value ? " active" : ""}`} onClick={() => setFilter(f.value)}>
              {f.label}
            </button>
          ))}
        </div>
        <table className="data-table">
          <thead>
            <tr>
              <th className="check-cell">
                <input
                  type="checkbox"
                  aria-label="Selecionar todos"
                  checked={allSelected}
                  disabled={deliveries.length === 0}
                  onChange={toggleAll}
                />
              </th>
              <th>Data</th>
              <th>Evento</th>
              <th>Status</th>
              <th>Tentativas</th>
              <th>Última resposta</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {deliveries.length === 0 && (
              <TableEmpty
                colSpan={7}
                title={filter ? "Nenhum envio com esse status" : "Nenhum envio ainda"}
                hint={filter ? "Escolha outro filtro acima." : "Com o webhook ativo, cada check-in aprovado aparece aqui."}
              />
            )}
            {deliveries.map((d) => (
              <tr key={d.id} className={selected.has(d.id) ? "row-selected" : undefined}>
                <td className="check-cell">
                  <input type="checkbox" aria-label="Selecionar envio" checked={selected.has(d.id)} onChange={() => toggle(d.id)} />
                </td>
                <td>{formatDateTime(d.createdAt)}</td>
                <td>
                  <span className="cell-strong">{d.summary ?? d.eventType}</span>
                  <br />
                  <span className="muted">{d.eventType}</span>
                </td>
                <td>
                  <span className={`badge badge-${STATUS[d.status].tone}`}>
                    <span className="dot" />
                    {STATUS[d.status].label}
                  </span>
                  {d.status === "Pending" && d.nextAttemptAt && (
                    <div className="muted" style={{ fontSize: "0.78rem", marginTop: 4 }}>próxima: {formatDateTime(d.nextAttemptAt)}</div>
                  )}
                </td>
                <td>{d.attempts}</td>
                <td className="muted" style={{ maxWidth: 280, wordBreak: "break-word" }}>
                  {d.status === "Delivered" ? `HTTP ${d.lastStatusCode}` : (d.lastError ?? "-")}
                </td>
                <td className="actions-cell">
                  {d.status !== "Delivered" && (
                    <button className="btn-link" disabled={busy} onClick={() => handleRetry(d.id)}>Reenviar</button>
                  )}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </section>

      <section className="panel">
        <h2>Formato do evento</h2>
        <p className="muted" style={{ marginTop: 0 }}>
          Para quem integra o sistema que recebe. <code>POST</code> com <code>Content-Type: application/json</code> e o
          header <code>X-Omniclub-Signature: sha256=&lt;hex&gt;</code> — HMAC-SHA256 do corpo cru com o segredo acima, em
          hex minúsculo. Responda <b>2xx em até 10 s</b>; fora disso o OmniClub reenvia em 1 min, 5 min, 30 min, 2 h e
          6 h, sempre com o mesmo <code>id</code> (use-o para ignorar duplicados). O botão de teste envia{" "}
          <code>"tipo": "webhook.teste"</code>, que deve ser respondido com 2xx e ignorado.
        </p>
        <pre className="code-block">{EXAMPLE_PAYLOAD}</pre>
        <p className="muted" style={{ marginBottom: 0 }}>
          <code>valorRepasse</code> vem do valor por check-in da unidade (em Pontos de check-in) e é <code>null</code> se
          não estiver configurado. <code>atletaEmail</code> é o e-mail informado pelo Wellhub — use <code>wellhubId</code>{" "}
          como chave alternativa.
        </p>
      </section>
    </div>
  );
}
