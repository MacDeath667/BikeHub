import { useEffect, useState } from "react";
import { api, query, requestLogin, userHref } from "../api";
import type { Community, Page, Ride, RouteData, User } from "../api";
import { Avatar, Empty, ErrorBox, Loading } from "../components/ui";
import { PostRoute, RouteEditor } from "../components/PostRoute";
import { MentionText } from "../components/MentionText";
import { ReportButton } from "../components/ContentTools";
import { rideDate, rideInstant, rideLocalTime } from "../components/rideTime";

const difficulties: Record<number, string> = { 1: "Легко", 2: "Средняя нагрузка", 3: "Сложно" };
function RideSummary({ ride, detail = false }: { ride: Ride; detail?: boolean }) {
  return <article className="profile-card ride-card"><div className="eyebrow">{ride.city} · {difficulties[ride.difficulty]}</div>
    {detail ? <h1>{ride.title}</h1> : <h2><a href={`#/ride/${ride.id}`}>{ride.title}</a></h2>}
    <p><time dateTime={ride.startsAt}>{rideDate(ride.startsAt, ride.timeZone)}</time> · {ride.timeZone}</p>
    <p>{ride.distanceKm} км{ride.averageSpeedKmh ? ` · ${ride.averageSpeedKmh} км/ч` : ""} · {ride.going}{ride.capacity ? ` из ${ride.capacity}` : ""} едут</p>
    <p className="muted">Встречаемся: {ride.meetingName}</p>
    {ride.groupId && <a className="text-button" href={`#/community/${ride.groupId}`}>{ride.groupName}</a>}
    {ride.status === 2 && <strong role="status">Покатушка отменена</strong>}
  </article>;
}
export function Rides({ me, groupId }: { me: User | null; groupId?: string | null }) {
  const [city, setCity] = useState(""), [filter, setFilter] = useState(""), [mine, setMine] = useState(false), [past, setPast] = useState(false);
  const [page, setPage] = useState(1), [retry, setRetry] = useState(0), [data, setData] = useState<Page<Ride> | null>(null), [error, setError] = useState("");
  useEffect(() => {
    let active = true; setData(null); setError("");
    api<Page<Ride>>(`/rides?${query({ city: filter, groupId: groupId || undefined, mine: String(mine), past: String(past), page, pageSize: 20 })}`)
      .then(result => { if (active) setData(result); }).catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [filter, mine, past, page, groupId, retry]);
  return <><div className="page-heading"><div className="eyebrow">ВМЕСТЕ ИНТЕРЕСНЕЕ</div><h1>Покатушки</h1><p>Найдите компанию, договоритесь о темпе и встречайтесь на старте.</p></div>
    <div className="content-tools"><button className="button primary" onClick={() => { if (!me) requestLogin(); else window.location.hash = `/ride/new${groupId ? `?group=${groupId}` : ""}`; }}>Организовать покатушку</button>{groupId && <a className="text-button" href="#/rides">Все сообщества</a>}</div>
    <form className="directory-search search-form" onSubmit={e => { e.preventDefault(); setFilter(city.trim()); setPage(1); }}><label>Город<input maxLength={100} value={city} onChange={e => setCity(e.target.value)} /></label><button className="text-button">Найти</button></form>
    <div className="content-tools ride-filters"><label><input type="checkbox" checked={mine} onChange={e => { if (!me) { requestLogin(); return; } setMine(e.target.checked); setPage(1); }} />Мои покатушки</label><label><input type="checkbox" checked={past} onChange={e => { setPast(e.target.checked); setPage(1); }} />Прошедшие</label></div>
    {error && <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />}
    {!data ? !error && <Loading /> : <>{data.items.map(ride => <RideSummary key={ride.id} ride={ride} />)}
      {!data.items.length && <Empty title="Здесь пока нет покатушек" description="Выберите другой город или организуйте свою встречу." />}
      {(page > 1 || data.hasMore) && <div className="content-tools"><button className="text-button" disabled={page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница {page}</span><button className="text-button" disabled={!data.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}</>}
  </>;
}
function RideEditor({ ride, group, saved, close }: { ride?: Ride; group: { id: string; name: string } | null; saved: (ride: Ride) => void; close: () => void }) {
  const [title, setTitle] = useState(ride?.title || ""), [description, setDescription] = useState(ride?.description || ""), [city, setCity] = useState(ride?.city || "");
  const [meetingName, setMeetingName] = useState(ride?.meetingName || ""), [lat, setLat] = useState(ride?.meetingPoint?.lat.toString() || ""), [lng, setLng] = useState(ride?.meetingPoint?.lng.toString() || "");
  const [zone, setZone] = useState(ride?.timeZone || Intl.DateTimeFormat().resolvedOptions().timeZone), [starts, setStarts] = useState(ride ? rideLocalTime(ride.startsAt, ride.timeZone) : "");
  const [distance, setDistance] = useState(String(ride?.distanceKm || "")), [speed, setSpeed] = useState(String(ride?.averageSpeedKmh || "")), [capacity, setCapacity] = useState(String(ride?.capacity || "")), [difficulty, setDifficulty] = useState(ride?.difficulty || 1);
  const [route, setRoute] = useState<RouteData | null>(ride?.route || null), [routeBusy, setRouteBusy] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState("");
  return <section className="profile-card group-editor"><h2>{ride ? "Изменить покатушку" : "Новая покатушка"}</h2>{group && <p>От сообщества <a href={`#/community/${group.id}`}>{group.name}</a></p>}
    <form onSubmit={async e => { e.preventDefault(); if (busy || routeBusy) return; setBusy(true); setError(""); try {
      if ((lat === "") !== (lng === "")) throw Error("Укажите обе координаты встречи или оставьте обе пустыми.");
      const result = await api<Ride>(`/rides${ride ? `/${ride.id}` : ""}`, { method: ride ? "PUT" : "POST", body: JSON.stringify({ title, description, city, meetingName,
        startsAt: rideInstant(starts, zone), timeZone: zone, distanceKm: Number(distance), averageSpeedKmh: speed ? Number(speed) : null,
        capacity: capacity ? Number(capacity) : null, difficulty, groupId: group?.id || null, meetingPoint: lat === "" ? null : { lat: Number(lat), lng: Number(lng) }, route, revision: ride?.revision || null }) }); saved(result);
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>
      <fieldset className="draft-fieldset" disabled={busy}>
        <label>Название<input required minLength={3} maxLength={160} value={title} onChange={e => setTitle(e.target.value)} /></label>
        <label>О поездке<textarea required maxLength={10000} value={description} onChange={e => setDescription(e.target.value)} placeholder="Что ждёт в пути, что взять с собой, где остановимся" /></label>
        <label>Город<input required maxLength={100} value={city} onChange={e => setCity(e.target.value)} /></label>
        <label>Дата и время встречи<input type="datetime-local" required value={starts} onChange={e => setStarts(e.target.value)} /></label>
        <label>Часовой пояс<input required maxLength={100} value={zone} onChange={e => setZone(e.target.value)} placeholder="Europe/Moscow" /></label><p className="form-hint">Время выше относится к этому часовому поясу, а не обязательно к поясу вашего устройства.</p>
        <label>Где встречаемся<input required maxLength={300} value={meetingName} onChange={e => setMeetingName(e.target.value)} placeholder="Точное место и ориентир" /></label>
        <div className="route-coordinate-inputs"><label>Широта встречи<input type="number" step="any" min={-90} max={90} value={lat} onChange={e => setLat(e.target.value)} /></label><label>Долгота встречи<input type="number" step="any" min={-180} max={180} value={lng} onChange={e => setLng(e.target.value)} /></label></div>
        <div className="route-coordinate-inputs"><label>Дистанция, км<input type="number" min={0} max={10000} step="0.1" required value={distance} onChange={e => setDistance(e.target.value)} /></label><label>Средний темп, км/ч<input type="number" min={1} max={100} step="0.1" value={speed} onChange={e => setSpeed(e.target.value)} /></label></div>
        <label>Нагрузка<select value={difficulty} onChange={e => setDifficulty(Number(e.target.value) as Ride["difficulty"])}>{Object.entries(difficulties).map(([id, text]) => <option key={id} value={id}>{text}</option>)}</select></label>
        <label>Всего мест, включая организатора<input type="number" min={1} max={10000} value={capacity} onChange={e => setCapacity(e.target.value)} placeholder="Без ограничения" /></label>
        <RouteEditor value={route} onChange={setRoute} onBusy={setRouteBusy} disabled={busy} />
        {route && (route.markers[0] || route.segments[0]?.[0]) && <button type="button" className="text-button" disabled={routeBusy} onClick={() => { const point = route.markers[0] || route.segments[0][0]; setLat(String(point.lat)); setLng(String(point.lng)); }}>Взять место встречи из начала маршрута</button>}
        {error && <ErrorBox message={error} />}<div className="content-tools"><button className="button primary" disabled={busy || routeBusy}>{busy ? "Сохраняем…" : ride ? "Сохранить изменения" : "Опубликовать покатушку"}</button><button type="button" className="text-button" disabled={busy || routeBusy} onClick={() => { if (window.confirm("Закрыть форму? Несохранённые изменения будут потеряны.")) close(); }}>Отмена</button></div>
      </fieldset>
    </form>
  </section>;
}
function RideMembers({ id, revision }: { id: string; revision: number }) {
  const [page, setPage] = useState(1), [retry, setRetry] = useState(0), [error, setError] = useState(""), [data, setData] = useState<Page<{ user: User; attendance: 1 | 2 }> | null>(null);
  useEffect(() => { let active = true; setData(null); setError(""); api<Page<{ user: User; attendance: 1 | 2 }>>(`/rides/${id}/participants?page=${page}&pageSize=20`).then(value => { if (active) setData(value); }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [id, page, retry, revision]);
  return <section className="profile-card"><h2>Участники</h2>{error && <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />}{!data ? !error && <Loading /> : <>{data.items.map(p => <div className="content-tools" key={p.user.id}><Avatar user={p.user} small /><a href={userHref(p.user)}>{p.user.name} @{p.user.handle}</a><span className="muted">{p.attendance === 1 ? "Едет" : "Возможно"}</span></div>)}{(page > 1 || data.hasMore) && <div className="content-tools"><button className="text-button" disabled={page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница {page}</span><button className="text-button" disabled={!data.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}</>}</section>;
}
export function RideDetail({ id, me, groupId }: { id: string; me: User | null; groupId?: string | null }) {
  const isNew = id === "new";
  const [ride, setRide] = useState<Ride | null>(null), [group, setGroup] = useState<Community | null>(null), [editing, setEditing] = useState(false), [members, setMembers] = useState(false);
  const [error, setError] = useState(""), [busy, setBusy] = useState(false), [retry, setRetry] = useState(0), [revision, setRevision] = useState(0);
  useEffect(() => { let active = true; setError("");
    if (isNew) { if (groupId) api<Community>(`/communities/${groupId}`).then(g => { if (active) setGroup(g); }).catch(e => { if (active) setError(e.message); }); }
    else api<Ride>(`/rides/${id}`).then(r => { if (active) setRide(r); }).catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [id, groupId, isNew, retry]);
  const save = (r: Ride) => { setRide(r); setEditing(false); setRevision(v => v + 1); if (isNew) window.location.hash = `/ride/${r.id}`; };
  async function mutate(path: string, method: string, body?: object) {
    if (busy) return; if (!me) { requestLogin(); return; } setBusy(true); setError("");
    try { save(await api<Ride>(`/rides/${id}/${path}`, { method, body: body ? JSON.stringify(body) : undefined })); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  if (isNew) {
    if (!me) return <Empty title="Организуйте покатушку" description="Войдите, чтобы пригласить участников." action={<button className="button primary" onClick={requestLogin}>Войти</button>} />;
    if (error) return <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />;
    if (groupId && !group) return <Loading />;
    return <><a className="back-link" href="#/rides">← Покатушки</a><RideEditor key={me.id} group={group ? { id: group.id, name: group.name } : null} saved={save} close={() => { window.location.hash = "/rides"; }} /></>;
  }
  if (!ride) return error ? <ErrorBox message={error} retry={() => setRetry(v => v + 1)} /> : <Loading />;
  const manage = me?.id === ride.organizer.id || me?.isModerator, closed = ride.status === 2 || Date.parse(ride.startsAt) <= Date.now();
  return <><a className="back-link" href="#/rides">← Все покатушки</a><RideSummary ride={ride} detail />
    {error && <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />}
    <div className="content-tools"><Avatar user={ride.organizer} /><span>Организатор: <a href={userHref(ride.organizer)}>{ride.organizer.name}</a></span></div>
    <p className="group-content"><MentionText text={ride.description} /></p>
    {ride.meetingPoint && <PostRoute route={{ name: "Место встречи", segments: [], markers: [{ ...ride.meetingPoint, label: ride.meetingName }] }} />}
    <PostRoute copyHref={`#/route/new?ride=${id}`} route={ride.route} />
    <div className="content-tools"><button className="text-button" onClick={() => setMembers(v => !v)}>{members ? "Скрыть участников" : `Участники: ${ride.going} едут · ${ride.maybe} возможно`}</button>
      {me?.id !== ride.organizer.id && !closed && <><button className={`button ${ride.attendance === 1 ? "primary" : "secondary"}`} disabled={busy} aria-pressed={ride.attendance === 1} onClick={() => void mutate("attendance", "PUT", { attendance: 1 })}>Еду</button><button className={`button ${ride.attendance === 2 ? "primary" : "secondary"}`} disabled={busy} aria-pressed={ride.attendance === 2} onClick={() => void mutate("attendance", "PUT", { attendance: 2 })}>Возможно</button></>}
      {ride.attendance && me?.id !== ride.organizer.id && <button className="text-button" disabled={busy} onClick={() => void mutate("attendance", "DELETE")}>Не участвую</button>}
      {manage && !closed && <><button className="text-button" disabled={busy} onClick={() => setEditing(v => !v)}>Изменить</button><button className="text-button" disabled={busy} onClick={() => { if (window.confirm("Отменить покатушку? Участники получат уведомление.")) void mutate("cancel", "POST", { revision: ride.revision }); }}>Отменить покатушку</button></>}
      {me && <ReportButton type={9} id={ride.id} notify={setError} />}
    </div>
    {members && <RideMembers id={id} revision={revision} />}
    {editing && manage && <RideEditor key={`${ride.id}-${ride.revision}-${me!.id}`} ride={ride} group={ride.groupId ? { id: ride.groupId, name: ride.groupName || "Сообщество" } : null} saved={save} close={() => setEditing(false)} />}
  </>;
}
