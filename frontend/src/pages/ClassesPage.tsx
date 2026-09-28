import { useEffect, useMemo, useState, type FormEvent } from "react";
import { createClass, createSlot, listClasses, listSlots, syncClass } from "../api/classes";
import { listCheckinPoints, syncCheckinPointProducts } from "../api/checkinPoints";
import type { CheckinPoint, ClassSlot, WellhubClass } from "../api/types";
import { extractErrorMessage } from "../api/client";
import { SyncBadge, TableEmpty } from "../components/ui";

const EMPTY_CLASS_FORM = { checkinPointId: "", productId: "", name: "", description: "" };
const EMPTY_SLOT_FORM = { date: "", startTime: "", endTime: "", capacity: "10", weeks: "1" };

function formatDateTime(value: string): string {
  return new Date(value).toLocaleString("pt-BR", { weekday: "short", day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit" });
}

function minutesBetween(start: string, end: string): number {
  return Math.round((new Date(end).getTime() - new Date(start).getTime()) / 60000);
}

/** "2026-10-01" + "18:30" no fuso do navegador → Date. */
function localDateTime(date: string, time: string): Date {
  const [y, m, d] = date.split("-").map(Number);
  const [hh, mm] = time.split(":").map(Number);
  return new Date(y!, m! - 1, d!, hh!, mm!);
}

export function ClassesPage() {
  const [points, setPoints] = useState<CheckinPoint[]>([]);
  const [classes, setClasses] = useState<WellhubClass[]>([]);
  const [classForm, setClassForm] = useState(EMPTY_CLASS_FORM);
  const [selectedClassId, setSelectedClassId] = useState<string | null>(null);
  const [slots, setSlots] = useState<ClassSlot[]>([]);
  const [slotForm, setSlotForm] = useState(EMPTY_SLOT_FORM);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const wellhubPoints = useMemo(() => points.filter((p) => p.app === "Wellhub" && p.active), [points]);
  const pointById = useMemo(() => new Map(points.map((p) => [p.id, p])), [points]);
  const selectedPoint = pointById.get(classForm.checkinPointId);
  const selectedClass = classes.find((c) => c.id === selectedClassId) ?? null;
  const pendingSlots = slots.filter((s) => !s.externalId && new Date(s.startsAt) > new Date());

  async function refreshClasses() {
    setClasses(await listClasses());
  }

  useEffect(() => {
    Promise.all([listCheckinPoints().then(setPoints), refreshClasses()])
      .catch((err) => setError(extractErrorMessage(err)));
  }, []);

  useEffect(() => {
    if (!selectedClassId) return;
    listSlots(selectedClassId).then(setSlots).catch((err) => setError(extractErrorMessage(err)));
  }, [selectedClassId]);

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

  function handleFetchProducts() {
    if (!selectedPoint) return;
    run(async () => {
      const updated = await syncCheckinPointProducts(selectedPoint.id);
      setPoints((prev) => prev.map((p) => (p.id === updated.id ? updated : p)));
    });
  }

  function handleCreateClass(e: FormEvent) {
    e.preventDefault();
    run(async () => {
      const created = await createClass({
        checkinPointId: classForm.checkinPointId,
        productId: Number(classForm.productId),
        name: classForm.name.trim(),
        description: classForm.description.trim() || null,
      });
      setClassForm(EMPTY_CLASS_FORM);
      await refreshClasses();
      setSelectedClassId(created.id);
      setNotice(created.externalId
        ? `Categoria "${created.name}" criada e publicada no Wellhub. Agora cadastre os horários abaixo.`
        : `Categoria "${created.name}" salva, mas ainda não chegou ao Wellhub. Use "Sincronizar" para tentar de novo.`);
    });
  }

  function handleSync(classId: string) {
    run(async () => {
      await syncClass(classId);
      await refreshClasses();
      if (classId === selectedClassId) setSlots(await listSlots(classId));
      setNotice("Sincronização com o Wellhub concluída.");
    });
  }

  function handleCreateSlots(e: FormEvent) {
    e.preventDefault();
    if (!selectedClassId) return;
    const weeks = Math.max(1, Math.min(52, Number(slotForm.weeks) || 1));
    const capacity = Number(slotForm.capacity);
    const firstStart = localDateTime(slotForm.date, slotForm.startTime);
    const firstEnd = localDateTime(slotForm.date, slotForm.endTime);
    if (firstEnd <= firstStart) {
      setNotice(null);
      setError("O horário de término precisa ser depois do início.");
      return;
    }

    run(async () => {
      let created = 0;
      let notSynced = 0;
      try {
        for (let week = 0; week < weeks; week++) {
          const start = new Date(firstStart);
          const end = new Date(firstEnd);
          start.setDate(start.getDate() + week * 7);
          end.setDate(end.getDate() + week * 7);
          const slot = await createSlot(selectedClassId, { startsAt: start.toISOString(), endsAt: end.toISOString(), capacity });
          created++;
          if (!slot.externalId) notSynced++;
        }
      } finally {
        // Se falhar no meio de uma repetição, a lista já mostra os que foram criados.
        setSlots(await listSlots(selectedClassId));
      }

      setSlotForm({ ...EMPTY_SLOT_FORM, capacity: slotForm.capacity });
      setNotice(notSynced === 0
        ? `${created} horário(s) criado(s) e publicado(s) no Wellhub.`
        : `${created} horário(s) criado(s); ${notSynced} ainda não chegou(aram) ao Wellhub — use "Sincronizar".`);
    });
  }

  return (
    <div>
      <div className="page-header">
        <h1>Agenda de aulas</h1>
        <p className="muted">
          Monte a grade de aulas da unidade. Tudo o que você cadastra aqui é publicado no Wellhub, e as reservas dos
          alunos são confirmadas automaticamente enquanto houver vaga.
        </p>
      </div>

      {error && <p className="error-text">{error}</p>}
      {notice && <p className="notice-text">{notice}</p>}

      <section className="panel">
        <h2>Nova categoria de aula</h2>
        <form className="form-grid" onSubmit={handleCreateClass}>
          <label>
            Unidade *
            <select
              value={classForm.checkinPointId}
              onChange={(e) => setClassForm({ ...classForm, checkinPointId: e.target.value, productId: "" })}
              required
            >
              <option value="">Selecione...</option>
              {wellhubPoints.map((p) => (
                <option key={p.id} value={p.id}>{p.name} ({p.externalId})</option>
              ))}
            </select>
          </label>
          <label>
            Produto Wellhub *
            <select
              value={classForm.productId}
              onChange={(e) => setClassForm({ ...classForm, productId: e.target.value })}
              disabled={!selectedPoint || selectedPoint.products.length === 0}
              required
            >
              <option value="">
                {!selectedPoint ? "Escolha a unidade primeiro" : selectedPoint.products.length === 0 ? "Nenhum produto vinculado" : "Selecione..."}
              </option>
              {selectedPoint?.products.map((prod) => (
                <option key={prod.productId} value={prod.productId}>{prod.name}{prod.virtual ? " (virtual)" : ""}</option>
              ))}
            </select>
          </label>
          <label>
            Nome *
            <input
              value={classForm.name}
              onChange={(e) => setClassForm({ ...classForm, name: e.target.value })}
              placeholder="Ex.: Tênis Iniciante"
              required
            />
          </label>
          <label>
            Descrição
            <input
              value={classForm.description}
              onChange={(e) => setClassForm({ ...classForm, description: e.target.value })}
              placeholder="Aparece para o aluno no app Wellhub"
            />
          </label>

          <div className="form-actions">
            <button type="submit" className="btn-primary" disabled={busy}>Criar categoria</button>
            {selectedPoint && selectedPoint.products.length === 0 && (
              <button type="button" className="btn-ghost" disabled={busy} onClick={handleFetchProducts}>
                Buscar produtos da unidade no Wellhub
              </button>
            )}
          </div>
        </form>
      </section>

      <section className="panel panel-table">
        <h2>Categorias</h2>
        <table className="data-table">
          <thead>
            <tr>
              <th>Nome</th>
              <th>Unidade</th>
              <th>Produto</th>
              <th>Wellhub</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {classes.length === 0 && (
              <TableEmpty colSpan={5} title="Nenhuma categoria de aula ainda" hint="Crie a primeira no formulário acima." />
            )}
            {classes.map((c) => {
              const point = pointById.get(c.checkinPointId);
              const product = point?.products.find((p) => p.productId === c.productId);
              return (
                <tr key={c.id} className={c.id === selectedClassId ? "row-selected" : undefined}>
                  <td className="cell-strong">{c.name}</td>
                  <td>{point?.name ?? "-"}</td>
                  <td>{product?.name ?? `#${c.productId}`}</td>
                  <td><SyncBadge synced={!!c.externalId} /></td>
                  <td className="actions-cell">
                    <button className="btn-link" onClick={() => setSelectedClassId(c.id)}>Horários</button>
                    {!c.externalId && (
                      <button className="btn-link" disabled={busy} onClick={() => handleSync(c.id)}>Sincronizar</button>
                    )}
                  </td>
                </tr>
              );
            })}
          </tbody>
        </table>
      </section>

      {selectedClass && (
        <>
          <section className="panel">
            <h2>Novo horário — {selectedClass.name}</h2>
            <form className="form-grid" onSubmit={handleCreateSlots}>
              <label>
                Data *
                <input type="date" value={slotForm.date} onChange={(e) => setSlotForm({ ...slotForm, date: e.target.value })} required />
              </label>
              <label>
                Início *
                <input type="time" value={slotForm.startTime} onChange={(e) => setSlotForm({ ...slotForm, startTime: e.target.value })} required />
              </label>
              <label>
                Término *
                <input type="time" value={slotForm.endTime} onChange={(e) => setSlotForm({ ...slotForm, endTime: e.target.value })} required />
              </label>
              <label>
                Vagas para Wellhub *
                <input type="number" min={1} value={slotForm.capacity} onChange={(e) => setSlotForm({ ...slotForm, capacity: e.target.value })} required />
              </label>
              <label>
                Repetir por (semanas)
                <input type="number" min={1} max={52} value={slotForm.weeks} onChange={(e) => setSlotForm({ ...slotForm, weeks: e.target.value })} />
              </label>
              <div className="form-actions">
                <button type="submit" className="btn-primary" disabled={busy}>
                  {Number(slotForm.weeks) > 1 ? `Criar ${slotForm.weeks} horários` : "Criar horário"}
                </button>
              </div>
            </form>
          </section>

          <section className="panel panel-table">
            <div className="panel-toolbar">
              <h2>Horários — {selectedClass.name}</h2>
              {selectedClass.externalId && pendingSlots.length > 0 && (
                <button className="btn-ghost" disabled={busy} onClick={() => handleSync(selectedClass.id)}>
                  Sincronizar {pendingSlots.length} pendente(s)
                </button>
              )}
            </div>
            <table className="data-table">
              <thead>
                <tr>
                  <th>Data/hora</th>
                  <th>Duração</th>
                  <th>Reservas</th>
                  <th>Wellhub</th>
                </tr>
              </thead>
              <tbody>
                {slots.length === 0 && (
                  <TableEmpty colSpan={4} title="Nenhum horário cadastrado" hint="Cadastre o primeiro no formulário acima." />
                )}
                {slots.map((s) => (
                  <tr key={s.id}>
                    <td className="cell-strong">{formatDateTime(s.startsAt)}</td>
                    <td>{minutesBetween(s.startsAt, s.endsAt)} min</td>
                    <td>{s.bookedCount} / {s.capacity}</td>
                    <td><SyncBadge synced={!!s.externalId} /></td>
                  </tr>
                ))}
              </tbody>
            </table>
          </section>
        </>
      )}
    </div>
  );
}
