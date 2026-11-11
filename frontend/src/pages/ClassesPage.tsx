import { useEffect, useMemo, useState, type FormEvent } from "react";
import { createClass, createSlot, listClasses, listSlots, listTotalPassPlans, syncClass } from "../api/classes";
import { listCheckinPoints, syncCheckinPointProducts } from "../api/checkinPoints";
import type { CheckinPoint, ClassSlot, TotalPassPlan, WellhubClass } from "../api/types";
import { extractErrorMessage } from "../api/client";
import { AppBadge, SyncBadge, TableEmpty } from "../components/ui";

interface ClassForm {
  checkinPointId: string;
  name: string;
  description: string;
  instructor: string;
  publishWellhub: boolean;
  productId: string;
  publishTotalPass: boolean;
  totalPassPlanId: string;
}

const EMPTY_CLASS_FORM: ClassForm = {
  checkinPointId: "",
  name: "",
  description: "",
  instructor: "",
  publishWellhub: false,
  productId: "",
  publishTotalPass: false,
  totalPassPlanId: "",
};
const EMPTY_SLOT_FORM = { date: "", startTime: "", endTime: "", capacity: "10", totalPassCapacity: "10", weeks: "1" };

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

/** Algo deste horário ainda não chegou a um dos apps em que a aula é publicada. */
function isPending(c: WellhubClass, s: ClassSlot): boolean {
  if (new Date(s.startsAt) <= new Date()) return false;
  return (c.publishWellhub && !s.externalId) || (c.publishTotalPass && !s.totalPassOccurrenceUuid);
}

export function ClassesPage() {
  const [points, setPoints] = useState<CheckinPoint[]>([]);
  const [classes, setClasses] = useState<WellhubClass[]>([]);
  const [classForm, setClassForm] = useState<ClassForm>(EMPTY_CLASS_FORM);
  const [plans, setPlans] = useState<TotalPassPlan[]>([]);
  const [selectedClassId, setSelectedClassId] = useState<string | null>(null);
  const [slots, setSlots] = useState<ClassSlot[]>([]);
  const [slotForm, setSlotForm] = useState(EMPTY_SLOT_FORM);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  const activePoints = useMemo(() => points.filter((p) => p.active && p.apps.length > 0), [points]);
  const pointById = useMemo(() => new Map(points.map((p) => [p.id, p])), [points]);
  const selectedPoint = pointById.get(classForm.checkinPointId);
  const pointHasWellhub = !!selectedPoint?.apps.includes("Wellhub");
  const pointHasTotalPass = !!selectedPoint?.apps.includes("TotalPass");
  const selectedClass = classes.find((c) => c.id === selectedClassId) ?? null;
  const pendingSlots = selectedClass ? slots.filter((s) => isPending(selectedClass, s)) : [];

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

  /** Trocar de unidade já marca os apps que ela aceita e busca os planos TotalPass dela. */
  function handleSelectPoint(pointId: string) {
    const point = pointById.get(pointId);
    const hasTotalPass = !!point?.apps.includes("TotalPass");
    setClassForm({
      ...classForm,
      checkinPointId: pointId,
      productId: "",
      totalPassPlanId: "",
      publishWellhub: !!point?.apps.includes("Wellhub"),
      publishTotalPass: hasTotalPass,
    });
    setPlans([]);
    if (point && hasTotalPass) {
      listTotalPassPlans(point.id)
        .then((list) => {
          setPlans(list);
          if (list.length === 1) setClassForm((f) => ({ ...f, totalPassPlanId: String(list[0]!.id) }));
        })
        .catch((err) => setError(extractErrorMessage(err)));
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
    if (!classForm.publishWellhub && !classForm.publishTotalPass) {
      setError("Escolha em qual app publicar a aula: Wellhub e/ou TotalPass.");
      return;
    }
    run(async () => {
      const created = await createClass({
        checkinPointId: classForm.checkinPointId,
        name: classForm.name.trim(),
        description: classForm.description.trim() || null,
        instructor: classForm.instructor.trim() || null,
        publishWellhub: classForm.publishWellhub,
        productId: classForm.publishWellhub ? Number(classForm.productId) : null,
        publishTotalPass: classForm.publishTotalPass,
        totalPassPlanId: classForm.publishTotalPass ? Number(classForm.totalPassPlanId) : null,
      });
      setClassForm(EMPTY_CLASS_FORM);
      setPlans([]);
      await refreshClasses();
      setSelectedClassId(created.id);
      const wellhubPending = created.publishWellhub && !created.externalId;
      setNotice(wellhubPending
        ? `Aula "${created.name}" salva, mas a categoria ainda não chegou ao Wellhub. Use "Sincronizar" para tentar de novo.`
        : `Aula "${created.name}" criada. Agora cadastre os horários abaixo — cada horário é publicado nos apps escolhidos.`);
    });
  }

  function handleSync(classId: string) {
    run(async () => {
      await syncClass(classId);
      await refreshClasses();
      if (classId === selectedClassId) setSlots(await listSlots(classId));
      setNotice("Sincronização com os apps concluída.");
    });
  }

  function handleCreateSlots(e: FormEvent) {
    e.preventDefault();
    if (!selectedClass) return;
    const classId = selectedClass.id;
    const weeks = Math.max(1, Math.min(52, Number(slotForm.weeks) || 1));
    const capacity = selectedClass.publishWellhub ? Number(slotForm.capacity) : 0;
    const totalPassCapacity = selectedClass.publishTotalPass ? Number(slotForm.totalPassCapacity) : 0;
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
          const slot = await createSlot(classId, { startsAt: start.toISOString(), endsAt: end.toISOString(), capacity, totalPassCapacity });
          created++;
          if (isPending(selectedClass, slot)) notSynced++;
        }
      } finally {
        // Se falhar no meio de uma repetição, a lista já mostra os que foram criados.
        setSlots(await listSlots(classId));
      }

      setSlotForm({ ...EMPTY_SLOT_FORM, capacity: slotForm.capacity, totalPassCapacity: slotForm.totalPassCapacity });
      setNotice(notSynced === 0
        ? `${created} horário(s) criado(s) e publicado(s).`
        : `${created} horário(s) criado(s); ${notSynced} ainda não chegou(aram) a algum app — use "Sincronizar".`);
    });
  }

  return (
    <div>
      <div className="page-header">
        <h1>Agenda de aulas</h1>
        <p className="muted">
          Monte a grade de aulas da unidade e publique no Wellhub, no TotalPass ou nos dois. As reservas chegam
          automaticamente em Reservas.
        </p>
      </div>

      {error && <p className="error-text">{error}</p>}
      {notice && <p className="notice-text">{notice}</p>}

      <section className="panel">
        <h2>Nova aula</h2>
        <form className="form-grid" onSubmit={handleCreateClass}>
          <label>
            Unidade *
            <select value={classForm.checkinPointId} onChange={(e) => handleSelectPoint(e.target.value)} required>
              <option value="">Selecione...</option>
              {activePoints.map((p) => (
                <option key={p.id} value={p.id}>{p.name} ({p.apps.join(" + ")})</option>
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
            Professor / responsável
            <input
              value={classForm.instructor}
              onChange={(e) => setClassForm({ ...classForm, instructor: e.target.value })}
              placeholder="Ex.: Prof. Ana"
            />
          </label>
          <label>
            Descrição
            <input
              value={classForm.description}
              onChange={(e) => setClassForm({ ...classForm, description: e.target.value })}
              placeholder="Aparece para o aluno no app"
            />
          </label>

          {pointHasWellhub && (
            <div className={`integration-block${classForm.publishWellhub ? " on" : ""}`}>
              <label className="checkbox-label integration-toggle">
                <input
                  type="checkbox"
                  checked={classForm.publishWellhub}
                  onChange={(e) => setClassForm({ ...classForm, publishWellhub: e.target.checked })}
                />
                <AppBadge app="Wellhub" /> Publicar no Wellhub
              </label>
              {classForm.publishWellhub && (
                <label>
                  Produto Wellhub *
                  <select
                    value={classForm.productId}
                    onChange={(e) => setClassForm({ ...classForm, productId: e.target.value })}
                    disabled={!selectedPoint || selectedPoint.products.length === 0}
                    required
                  >
                    <option value="">{selectedPoint?.products.length === 0 ? "Nenhum produto vinculado" : "Selecione..."}</option>
                    {selectedPoint?.products.map((prod) => (
                      <option key={prod.productId} value={prod.productId}>{prod.name}{prod.virtual ? " (virtual)" : ""}</option>
                    ))}
                  </select>
                </label>
              )}
              {classForm.publishWellhub && selectedPoint && selectedPoint.products.length === 0 && (
                <div>
                  <button type="button" className="btn-ghost" disabled={busy} onClick={handleFetchProducts}>
                    Buscar produtos da unidade no Wellhub
                  </button>
                </div>
              )}
            </div>
          )}

          {pointHasTotalPass && (
            <div className={`integration-block${classForm.publishTotalPass ? " on" : ""}`}>
              <label className="checkbox-label integration-toggle">
                <input
                  type="checkbox"
                  checked={classForm.publishTotalPass}
                  onChange={(e) => setClassForm({ ...classForm, publishTotalPass: e.target.checked })}
                />
                <AppBadge app="TotalPass" /> Publicar no TotalPass
              </label>
              {classForm.publishTotalPass && (
                <label>
                  Plano TotalPass *
                  <select
                    value={classForm.totalPassPlanId}
                    onChange={(e) => setClassForm({ ...classForm, totalPassPlanId: e.target.value })}
                    disabled={plans.length === 0}
                    required
                  >
                    <option value="">{plans.length === 0 ? "Buscando planos..." : "Selecione..."}</option>
                    {plans.map((plan) => (
                      <option key={plan.id} value={plan.id}>{plan.name}{plan.code ? ` (${plan.code})` : ""}</option>
                    ))}
                  </select>
                </label>
              )}
              {classForm.publishTotalPass && (
                <p className="muted integration-hint">
                  No TotalPass, o responsável aparece para o aluno — sem preencher, vai como "Equipe {selectedPoint?.name}".
                  Vagas do TotalPass só podem aumentar depois de publicadas.
                </p>
              )}
            </div>
          )}

          <div className="form-actions">
            <button type="submit" className="btn-primary" disabled={busy || !selectedPoint}>Criar aula</button>
          </div>
        </form>
      </section>

      <section className="panel panel-table">
        <h2>Aulas</h2>
        <table className="data-table">
          <thead>
            <tr>
              <th>Nome</th>
              <th>Unidade</th>
              <th>Publicada em</th>
              <th>Responsável</th>
              <th></th>
            </tr>
          </thead>
          <tbody>
            {classes.length === 0 && (
              <TableEmpty colSpan={5} title="Nenhuma aula ainda" hint="Crie a primeira no formulário acima." />
            )}
            {classes.map((c) => {
              const point = pointById.get(c.checkinPointId);
              const product = point?.products.find((p) => p.productId === c.productId);
              const wellhubPending = c.publishWellhub && !c.externalId;
              return (
                <tr key={c.id} className={c.id === selectedClassId ? "row-selected" : undefined}>
                  <td className="cell-strong">{c.name}</td>
                  <td>{point?.name ?? "-"}</td>
                  <td>
                    <div className="cell-stack">
                      {c.publishWellhub && (
                        <span className="app-badges">
                          <AppBadge app="Wellhub" />
                          <span className="muted">{product?.name ?? `#${c.productId}`}</span>
                          {wellhubPending && <SyncBadge synced={false} />}
                        </span>
                      )}
                      {c.publishTotalPass && <AppBadge app="TotalPass" />}
                    </div>
                  </td>
                  <td className={c.instructor ? undefined : "muted"}>{c.instructor ?? "—"}</td>
                  <td className="actions-cell">
                    <button className="btn-link" onClick={() => setSelectedClassId(c.id)}>Horários</button>
                    {wellhubPending && (
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
              {selectedClass.publishWellhub && (
                <label>
                  Vagas Wellhub *
                  <input type="number" min={1} value={slotForm.capacity} onChange={(e) => setSlotForm({ ...slotForm, capacity: e.target.value })} required />
                </label>
              )}
              {selectedClass.publishTotalPass && (
                <label>
                  Vagas TotalPass *
                  <input type="number" min={1} value={slotForm.totalPassCapacity} onChange={(e) => setSlotForm({ ...slotForm, totalPassCapacity: e.target.value })} required />
                </label>
              )}
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
              {pendingSlots.length > 0 && (
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
                  {selectedClass.publishWellhub && <th>Wellhub</th>}
                  {selectedClass.publishTotalPass && <th>TotalPass</th>}
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
                    {selectedClass.publishWellhub && (
                      <td>
                        <span className="app-badges">
                          <span>{s.bookedCount} / {s.capacity}</span>
                          <SyncBadge synced={!!s.externalId} />
                        </span>
                      </td>
                    )}
                    {selectedClass.publishTotalPass && (
                      <td>
                        <span className="app-badges">
                          <span>{s.totalPassBookedCount} / {s.totalPassCapacity}</span>
                          <SyncBadge synced={!!s.totalPassOccurrenceUuid} />
                        </span>
                      </td>
                    )}
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
