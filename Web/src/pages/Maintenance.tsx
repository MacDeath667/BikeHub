import { useEffect, useRef, useState } from "react";
import { api, requestLogin } from "../api";
import type { BikeComponent, MaintenanceOverview, MaintenanceRecord, Page, User } from "../api";
import { Empty, ErrorBox, Loading } from "../components/ui";
import { rideLocalTime } from "../components/rideTime";

const componentKinds: Record<number, string> = { 1: "Цепь", 2: "Кассета", 3: "Покрышки", 4: "Тормоза", 5: "Трансмиссия", 6: "Вилка", 7: "Амортизатор", 8: "Колёса", 9: "Другое" };
const workKinds: Record<number, string> = { 1: "Обслуживание", 2: "Замена", 3: "Осмотр", 4: "Смазка", 5: "Ремонт", 6: "Другое" };
type ComponentOption = { id: string; name: string; isArchived: boolean };
const zone = () => Intl.DateTimeFormat().resolvedOptions().timeZone;
const today = () => rideLocalTime(new Date().toISOString(), zone()).slice(0, 10);
function useResource<T>(endpoint: string, revision: number) {
  const [data, setData] = useState<T | null>(null), [error, setError] = useState(""), [loading, setLoading] = useState(true), [retry, setRetry] = useState(0);
  const previousEndpoint = useRef(endpoint);
  useEffect(() => { let active = true; setLoading(true); setError(""); if (previousEndpoint.current !== endpoint) { setData(null); previousEndpoint.current = endpoint; }
    api<T>(endpoint).then(value => { if (active) setData(value); }).catch(e => { if (active) setError(e.message); }).finally(() => { if (active) setLoading(false); }); return () => { active = false; };
  }, [endpoint, revision, retry]);
  return { data, error, loading, retry: () => setRetry(v => v + 1) };
}
function ComponentEditor({ base, value, mileage, saved, close }: { base: string; value?: BikeComponent; mileage: number; saved: () => void; close: () => void }) {
  const [kind, setKind] = useState(value?.kind || 1), [name, setName] = useState(value?.name || "Цепь"), [model, setModel] = useState(value?.model || ""), [notes, setNotes] = useState(value?.notes || "");
  const [installedMileage, setInstalledMileage] = useState(String(value?.installedMileage ?? mileage)), [date, setDate] = useState(value?.installedOn || today()), [interval, setInterval] = useState(String(value?.serviceIntervalKm || "")), [archived, setArchived] = useState(value?.isArchived || false);
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  return <section className="profile-card group-editor"><h2>{value ? "Изменить компонент" : "Новый компонент"}</h2><form onSubmit={async e => { e.preventDefault(); if (busy) return; setBusy(true); setError(""); try {
    await api(`${base}/components${value ? `/${value.id}` : ""}`, { method: value ? "PUT" : "POST", body: JSON.stringify({ kind, name, model, notes, installedMileage: Number(installedMileage), installedOn: date, serviceIntervalKm: interval ? Number(interval) : null, isArchived: archived, revision: value?.revision || null, timeZone: zone() }) }); saved();
  } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}><fieldset className="draft-fieldset" disabled={busy}>
    <label>Тип компонента<select value={kind} onChange={e => { const next = Number(e.target.value); if (name === componentKinds[kind]) setName(componentKinds[next]); setKind(next); }}>{Object.entries(componentKinds).map(([id, text]) => <option key={id} value={id}>{text}</option>)}</select></label>
    <label>Название компонента<input required minLength={2} maxLength={100} value={name} onChange={e => setName(e.target.value)} /></label><label>Модель<input maxLength={100} value={model} onChange={e => setModel(e.target.value)} /></label>
    <label>Пробег при установке, км<input type="number" required min={0} max={mileage} step="0.1" value={installedMileage} onChange={e => setInstalledMileage(e.target.value)} /></label><label>Дата установки<input type="date" required min="1900-01-01" max={today()} value={date} onChange={e => setDate(e.target.value)} /></label>
    <label>Интервал обслуживания, км<input type="number" min={1} max={100000} step="0.1" value={interval} onChange={e => setInterval(e.target.value)} placeholder="Без напоминаний" /></label><p className="form-hint">Интервал задаёте вы по инструкции производителя или своему опыту. Счёт начинается с установки или последней отмеченной работы.</p>
    <label>Личная заметка о компоненте<textarea maxLength={2000} value={notes} onChange={e => setNotes(e.target.value)} /></label>
    {value && <label className="maintenance-check"><input type="checkbox" checked={archived} onChange={e => setArchived(e.target.checked)} />В архиве: не напоминать, историю сохранить</label>}
    {error && <ErrorBox message={error} />}<div className="content-tools"><button className="button primary" disabled={busy}>Сохранить компонент</button><button type="button" className="text-button" disabled={busy} onClick={() => { if (window.confirm("Закрыть без сохранения изменений?")) close(); }}>Отмена</button></div>
  </fieldset></form></section>;
}
function RecordEditor({ base, value, components, mileage, selectedComponent, saved, close }: { base: string; value?: MaintenanceRecord; components: ComponentOption[]; mileage: number; selectedComponent?: string; saved: () => void; close: () => void }) {
  const [kind, setKind] = useState(value?.kind || 1), [title, setTitle] = useState(value?.title || ""), [notes, setNotes] = useState(value?.notes || ""), [component, setComponent] = useState(value?.componentId || selectedComponent || "");
  const [date, setDate] = useState(value?.performedOn || today()), [atMileage, setMileage] = useState(String(value?.mileage ?? mileage)), [cost, setCost] = useState(value?.costRub?.toString() || ""), [reset, setReset] = useState(value?.resetsInterval || !!selectedComponent);
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  return <section className="profile-card group-editor"><h2>{value ? "Изменить работу" : "Записать работу"}</h2><form onSubmit={async e => { e.preventDefault(); if (busy) return; setBusy(true); setError(""); try {
    await api(`${base}/records${value ? `/${value.id}` : ""}`, { method: value ? "PUT" : "POST", body: JSON.stringify({ kind, title, notes, performedOn: date, mileage: Number(atMileage), componentId: component || null, costRub: cost ? Number(cost) : null, resetsInterval: !!component && reset, revision: value?.revision || null, timeZone: zone() }) }); saved();
  } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}><fieldset className="draft-fieldset" disabled={busy}>
    <label>Вид работы<select value={kind} onChange={e => { const next = Number(e.target.value); setKind(next); setReset(next === 1 || next === 2); }}>{Object.entries(workKinds).map(([id, text]) => <option key={id} value={id}>{text}</option>)}</select></label>
    <label>Что сделано<input required minLength={2} maxLength={160} value={title} onChange={e => setTitle(e.target.value)} /></label><label>Компонент<select value={component} onChange={e => { setComponent(e.target.value); if (!e.target.value) setReset(false); }}>{<option value="">Велосипед целиком</option>}{components.map(c => <option key={c.id} value={c.id} disabled={c.isArchived && value?.componentId !== c.id}>{c.name}{c.isArchived ? " (в архиве)" : ""}</option>)}</select></label>
    <label>Дата работы<input type="date" required min="1900-01-01" max={today()} value={date} onChange={e => setDate(e.target.value)} /></label><label>Пробег при работе, км<input type="number" required min={0} max={1000000} step="0.1" value={atMileage} onChange={e => setMileage(e.target.value)} /></label><p className="form-hint">Если пробег больше текущего, одометр велосипеда обновится. Историческая запись с меньшим пробегом его не уменьшит.</p>
    <label>Стоимость, ₽<input type="number" min={0} max={10000000} step="0.01" value={cost} onChange={e => setCost(e.target.value)} /></label><label>Личная заметка о работе<textarea maxLength={4000} value={notes} onChange={e => setNotes(e.target.value)} /></label>
    <label className="maintenance-check"><input type="checkbox" checked={!!component && reset} disabled={!component} onChange={e => setReset(e.target.checked)} />Начать новый интервал обслуживания этого компонента</label>
    {error && <ErrorBox message={error} />}<div className="content-tools"><button className="button primary" disabled={busy}>Сохранить работу</button><button type="button" className="text-button" disabled={busy} onClick={() => { if (window.confirm("Закрыть без сохранения изменений?")) close(); }}>Отмена</button></div>
  </fieldset></form></section>;
}
function Components({ base, revision, blocked, edit, record }: { base: string; revision: number; blocked: boolean; edit: (c: BikeComponent) => void; record: (id: string) => void }) {
  const [archived, setArchived] = useState(false), [page, setPage] = useState(1);
  const resource = useResource<Page<BikeComponent>>(`${base}/components?archived=${archived}&page=${page}&pageSize=20`, revision);
  return <section><h2>Компоненты</h2><label className="maintenance-check"><input type="checkbox" checked={archived} disabled={blocked || resource.loading} onChange={e => { setArchived(e.target.checked); setPage(1); }} />Показать архив</label>
    {resource.error && <ErrorBox message={resource.error} retry={resource.retry} />}{resource.loading && !resource.data && <Loading />}
    {resource.data?.items.map(c => <article className={`profile-card${c.isDue ? " maintenance-due" : ""}`} key={c.id}><div className="eyebrow">{componentKinds[c.kind]}{c.isArchived ? " · В архиве" : ""}</div><h3>{c.name}</h3><p>{c.model}</p><p>Установлен: {c.installedOn} · {c.installedMileage} км</p>
      <p>{c.dueMileage === null ? "Напоминание не настроено" : c.isDue ? `Пора обслужить: пробег ${c.dueMileage} км достигнут` : `Следующее обслуживание: ${c.dueMileage} км · осталось ${Math.max(0, c.dueMileage - c.currentMileage).toLocaleString("ru-RU")} км`}</p>
      {c.notes && <p className="group-content">{c.notes}</p>}<div className="content-tools"><button className="text-button" disabled={blocked || resource.loading} onClick={() => edit(c)}>Изменить компонент</button>{!c.isArchived && <button className="text-button" disabled={blocked || resource.loading} onClick={() => record(c.id)}>Записать обслуживание</button>}</div></article>)}
    {resource.data && !resource.data.items.length && <p className="muted">{archived ? "Архив пуст." : "Добавьте цепь, тормоза и другие компоненты, за которыми хотите следить."}</p>}
    {resource.data && (page > 1 || resource.data.hasMore) && <div className="content-tools"><button className="text-button" disabled={resource.loading || blocked || page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница компонентов {page}</span><button className="text-button" disabled={resource.loading || blocked || !resource.data.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}
  </section>;
}
function History({ base, revision, blocked, edit, changed }: { base: string; revision: number; blocked: boolean; edit: (r: MaintenanceRecord) => void; changed: () => void }) {
  const [page, setPage] = useState(1), [busy, setBusy] = useState(false), [error, setError] = useState("");
  const resource = useResource<Page<MaintenanceRecord>>(`${base}/records?page=${page}&pageSize=20`, revision);
  return <section><h2>История работ</h2>{error && <ErrorBox message={error} />}{resource.error && <ErrorBox message={resource.error} retry={resource.retry} />}{resource.loading && !resource.data && <Loading />}
    {resource.data?.items.map(r => <article className="profile-card" key={r.id}><div className="eyebrow">{workKinds[r.kind]} · {r.performedOn} · {r.mileage} км</div><h3>{r.title}</h3>{r.componentName && <p>{r.componentName}{r.resetsInterval ? " · Новый интервал обслуживания" : ""}</p>}
      {r.costRub !== null && <p>{r.costRub.toLocaleString("ru-RU")} ₽</p>}{r.notes && <p className="group-content">{r.notes}</p>}<div className="content-tools"><button className="text-button" disabled={blocked || busy || resource.loading} onClick={() => edit(r)}>Изменить работу</button><button className="text-button" disabled={blocked || busy || resource.loading} onClick={async () => { if (!window.confirm("Удалить запись о работе? Напоминания будут пересчитаны, одометр не уменьшится.")) return; setBusy(true); setError(""); try { await api(`${base}/records/${r.id}`, { method: "DELETE" }); changed(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>Удалить работу</button></div>
    </article>)}
    {resource.data && !resource.data.items.length && <p className="muted">Запишите первую выполненную работу — история останется у этого велосипеда.</p>}
    {resource.data && (page > 1 || resource.data.hasMore) && <div className="content-tools"><button className="text-button" disabled={resource.loading || busy || blocked || page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница работ {page}</span><button className="text-button" disabled={resource.loading || busy || blocked || !resource.data.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}
  </section>;
}
export function Maintenance({ bikeId, me }: { bikeId: string; me: User | null }) {
  // The account key in App remounts this private page on sign-in changes.
  return me ? <MaintenancePage bikeId={bikeId} /> : <Empty title="Обслуживание вашего велосипеда" description="История, заметки и расходы видны только владельцу. Войдите для просмотра." action={<button className="button primary" onClick={requestLogin}>Войти</button>} />;
}
function MaintenancePage({ bikeId }: { bikeId: string }) {
  const base = `/bikes/${bikeId}/maintenance`;
  const [revision, setRevision] = useState(0), [mileage, setMileage] = useState(""), [busy, setBusy] = useState(false), [error, setError] = useState("");
  const [component, setComponent] = useState<BikeComponent | "new" | null>(null), [record, setRecord] = useState<MaintenanceRecord | "new" | null>(null), [selectedComponent, setSelectedComponent] = useState<string | undefined>();
  const overview = useResource<MaintenanceOverview>(base, revision), options = useResource<ComponentOption[]>(`${base}/components/options`, revision);
  useEffect(() => { if (overview.data) setMileage(String(overview.data.mileage)); }, [overview.data?.mileage]);
  const changed = () => { setComponent(null); setRecord(null); setSelectedComponent(undefined); setRevision(v => v + 1); };
  const editing = !!component || !!record;
  if (!overview.data) return overview.error ? <ErrorBox message={overview.error} retry={overview.retry} /> : <Loading />;
  return <><a className="back-link" href={`#/bike/${bikeId}`}>← К велосипеду</a><div className="page-heading"><div className="eyebrow">ЛИЧНЫЙ ЖУРНАЛ ВЛАДЕЛЬЦА</div><h1>Обслуживание: {overview.data.bikeName}</h1><p>{overview.data.activeComponents} компонентов · {overview.data.dueComponents} ждут обслуживания</p></div>
    {overview.error && <ErrorBox message={overview.error} retry={overview.retry} />}<p className="form-hint">Расходы и заметки здесь видны только вам. Записи в публичном бортжурнале публикуются отдельно.</p>
    <form className="profile-card directory-search search-form" onSubmit={async e => { e.preventDefault(); if (busy || editing) return; if (Number(mileage) < overview.data!.mileage && !window.confirm("Уменьшить одометр? Записи о прошлых работах сохранят свой пробег.")) return; setBusy(true); setError(""); try { await api(`${base}/mileage`, { method: "PUT", body: JSON.stringify({ mileage: Number(mileage) }) }); changed(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}><label>Текущий пробег, км<input type="number" required min={0} max={1000000} step="0.1" value={mileage} disabled={busy || editing} onChange={e => setMileage(e.target.value)} /></label><button className="text-button" disabled={busy || editing || overview.loading}>Обновить пробег</button></form>
    {error && <ErrorBox message={error} />}{options.error && <ErrorBox message={options.error} retry={options.retry} />}
    <div className="content-tools"><button className="button secondary" disabled={editing || busy} onClick={() => setComponent("new")}>Добавить компонент</button><button className="button primary" disabled={editing || busy || !options.data} onClick={() => setRecord("new")}>Записать работу</button></div>
    {component && <ComponentEditor key={component === "new" ? "new" : component.id} base={base} value={component === "new" ? undefined : component} mileage={overview.data.mileage} saved={changed} close={() => setComponent(null)} />}
    {record && options.data && <RecordEditor key={record === "new" ? `new-${selectedComponent || "all"}` : record.id} base={base} value={record === "new" ? undefined : record} selectedComponent={selectedComponent} components={options.data} mileage={overview.data.mileage} saved={changed} close={() => { setRecord(null); setSelectedComponent(undefined); }} />}
    <Components base={base} revision={revision} blocked={editing || busy} edit={setComponent} record={id => { if (!options.data) return; setSelectedComponent(id); setRecord("new"); }} />
    <History base={base} revision={revision} blocked={editing || busy || !options.data} edit={setRecord} changed={changed} />
  </>;
}
