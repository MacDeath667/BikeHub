import { useEffect, useState } from "react";
import { CityInput } from "../components/CityInput";
import { api, query, requestLogin, userHref } from "../api";
import type { LibraryRoute, Page, RouteCollection, RouteData, RouteReview, User } from "../api";
import { Avatar, Empty, ErrorBox, Loading } from "../components/ui";
import { PostRoute, RouteEditor } from "../components/PostRoute";
import { RouteCover } from "../components/RouteCover";
import { MentionText } from "../components/MentionText";
import { ReportButton } from "../components/ContentTools";
import { rideLocalTime } from "../components/rideTime";

const surfaces: Record<number, string> = { 1: "Асфальт", 2: "Гравий", 3: "Тропы", 4: "Смешанное" };
function Collections({ items, changed, selected, onSelect }: { items: RouteCollection[]; changed: () => void; selected: string; onSelect: (id: string) => void }) {
  const [name, setName] = useState(""), [editing, setEditing] = useState<string | null>(null), [busy, setBusy] = useState(false), [error, setError] = useState("");
  return <details className="profile-card route-collections"><summary>Мои подборки</summary>
    {items.map(item => <div className="content-tools" key={item.id}><button className="text-button" aria-pressed={selected === item.id} onClick={() => onSelect(selected === item.id ? "" : item.id)}>{item.name} · {item.routesCount}</button>
      <button className="text-button" disabled={busy} onClick={() => { setEditing(item.id); setName(item.name); }}>Переименовать</button><button className="text-button" disabled={busy} onClick={async () => { if (!window.confirm("Удалить подборку? Сохранённые маршруты и заметки останутся.")) return; setBusy(true); setError(""); try { await api(`/route-collections/${item.id}`, { method: "DELETE" }); if (selected === item.id) onSelect(""); changed(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>Удалить подборку</button></div>)}
    <form className="directory-search search-form" onSubmit={async e => { e.preventDefault(); if (busy) return; setBusy(true); setError(""); try {
      await api(`/route-collections${editing ? `/${editing}` : ""}`, { method: editing ? "PUT" : "POST", body: JSON.stringify({ name }) }); setName(""); setEditing(null); changed();
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}><label>{editing ? "Название подборки" : "Новая подборка"}<input minLength={2} maxLength={80} required value={name} disabled={busy} onChange={e => setName(e.target.value)} /></label><button className="text-button" disabled={busy}>Сохранить подборку</button>{editing && <button type="button" className="text-button" disabled={busy} onClick={() => { setEditing(null); setName(""); }}>Отмена</button>}</form>
    {error && <ErrorBox message={error} />}
  </details>;
}
export function RouteLibrary({ me }: { me: User | null }) {
  const [city, setCity] = useState(""), [surface, setSurface] = useState(""), [minimum, setMinimum] = useState(""), [maximum, setMaximum] = useState("");
  const [filters, setFilters] = useState({ city: "", surface: "", minimum: "", maximum: "" }), [mine, setMine] = useState(false), [saved, setSaved] = useState(false), [collection, setCollection] = useState("");
  const [collections, setCollections] = useState<RouteCollection[]>([]), [collectionsError, setCollectionsError] = useState(""), [collectionsRevision, setCollectionsRevision] = useState(0);
  const [data, setData] = useState<Page<LibraryRoute> | null>(null), [page, setPage] = useState(1), [retry, setRetry] = useState(0), [error, setError] = useState("");
  useEffect(() => { let active = true; setData(null); setError(""); api<Page<LibraryRoute>>(`/route-library?${query({ ...filters, mine: String(mine), saved: String(saved), collectionId: collection || undefined, page, pageSize: 12 })}`).then(d => { if (active) setData(d); }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [filters, mine, saved, collection, page, retry, collectionsRevision]);
  useEffect(() => { let active = true; setCollectionsError(""); if (me) api<RouteCollection[]>("/route-collections").then(c => { if (active) setCollections(c); }).catch(e => { if (active) setCollectionsError(e.message); }); return () => { active = false; }; }, [me?.id, collectionsRevision]);
  const select = (id: string) => { setCollection(id); setSaved(!!id); setPage(1); };
  return <><div className="page-heading"><div className="eyebrow">ЕСТЬ КУДА ПОЕХАТЬ</div><h1>Маршруты</h1><p>GPX, проверенные дороги и личные подборки для будущих поездок.</p></div>
    <button className="button primary" onClick={() => { if (!me) requestLogin(); else window.location.hash = "/route/new"; }}>Добавить маршрут</button>
    <form className="profile-card group-editor" onSubmit={e => { e.preventDefault(); setFilters({ city: city.trim(), surface, minimum, maximum }); setPage(1); }}>
      <label>Город маршрута<CityInput aria-label="Город маршрута" value={city} onValueChange={setCity} placeholder="Любой город" /></label>
      <label>Покрытие<select value={surface} onChange={e => setSurface(e.target.value)}><option value="">Любое</option>{Object.entries(surfaces).map(([key, value]) => <option key={key} value={key}>{value}</option>)}</select></label>
      <div className="route-coordinate-inputs"><label>Дистанция от, км<input type="number" min={0} max={100000} value={minimum} onChange={e => setMinimum(e.target.value)} /></label><label>Дистанция до, км<input type="number" min={0} max={100000} value={maximum} onChange={e => setMaximum(e.target.value)} /></label></div><button className="text-button">Найти маршруты</button>
    </form>
    <div className="content-tools ride-filters"><label><input type="checkbox" checked={mine} onChange={e => { if (!me) { requestLogin(); return; } setMine(e.target.checked); setPage(1); }} />Мои публикации</label><label><input type="checkbox" checked={saved} onChange={e => { if (!me) { requestLogin(); return; } setSaved(e.target.checked); if (!e.target.checked) setCollection(""); setPage(1); }} />Сохранённые</label></div>
    {collectionsError && <ErrorBox message={collectionsError} retry={() => setCollectionsRevision(v => v + 1)} />}
    {me && <Collections items={collections} selected={collection} onSelect={select} changed={() => setCollectionsRevision(v => v + 1)} />}
    {collection && <button className="text-button" onClick={() => select("")}>Сбросить подборку</button>}
    {error && <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />}{!data ? !error && <Loading /> : <>
      {data.items.map(route => <article className="profile-card route-library-card" key={route.id}><div className="eyebrow">{route.city} · {surfaces[route.surface]}</div><h2><a href={`#/route/${route.id}`}>{route.title}</a></h2>
        <p>{route.distanceKm.toLocaleString("ru-RU")} км · {route.rating ? `★ ${route.rating.toFixed(1)}` : "Пока без оценок"} · {route.reviewsCount} отзывов{route.isSaved ? " · Сохранён" : ""}</p>
        <RouteCover previewUrl={`/route-library/${route.id}/preview`} href={`#/route/${route.id}`} /><p><a href={userHref(route.author)}>{route.author.name}</a></p>
      </article>)}
      {!data.items.length && <Empty title="Маршрутов пока нет" description="Попробуйте другой фильтр или поделитесь своей дорогой." />}
      {(page > 1 || data.hasMore) && <div className="content-tools"><button className="text-button" disabled={page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница {page}</span><button className="text-button" disabled={!data.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}
    </>}
  </>;
}
function LibraryEditor({ value, initialRoute, saved, close }: { value?: LibraryRoute; initialRoute?: RouteData | null; saved: (route: LibraryRoute) => void; close: () => void }) {
  const [title, setTitle] = useState(value?.title || initialRoute?.name || ""), [description, setDescription] = useState(value?.description || ""), [city, setCity] = useState(value?.city || ""), [surface, setSurface] = useState(value?.surface || 4);
  const [route, setRoute] = useState<RouteData | null>(value?.route || initialRoute || null), [routeBusy, setRouteBusy] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState("");
  return <section className="profile-card group-editor"><h2>{value ? "Изменить маршрут" : "Новый маршрут"}</h2><form onSubmit={async e => { e.preventDefault(); if (busy || routeBusy) return; if (!route) { setError("Добавьте GPX или точки на карте."); return; } setBusy(true); setError(""); try {
    saved(await api<LibraryRoute>(`/route-library${value ? `/${value.id}` : ""}`, { method: value ? "PUT" : "POST", body: JSON.stringify({ title, description, city, surface, route, revision: value?.revision || null }) }));
  } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}><fieldset className="draft-fieldset" disabled={busy}>
    <label>Название в библиотеке<input required minLength={3} maxLength={160} value={title} onChange={e => setTitle(e.target.value)} /></label><label>О маршруте<textarea required maxLength={10000} value={description} onChange={e => setDescription(e.target.value)} placeholder="Дорога, сезон, вода, сложные участки" /></label>
    <label>Город<CityInput required value={city} onValueChange={setCity} placeholder="Начните вводить название" /></label><label>Основное покрытие<select value={surface} onChange={e => setSurface(Number(e.target.value) as LibraryRoute["surface"])}>{Object.entries(surfaces).map(([key, label]) => <option key={key} value={key}>{label}</option>)}</select></label>
    <RouteEditor value={route} onChange={setRoute} onBusy={setRouteBusy} disabled={busy} /><p className="form-hint">Дистанция считается по линиям маршрута. Отдельные метки не соединяются автоматически.</p>
    {error && <ErrorBox message={error} />}<div className="content-tools"><button className="button primary" disabled={busy || routeBusy}>Опубликовать маршрут</button><button type="button" className="text-button" disabled={busy || routeBusy} onClick={() => { if (window.confirm("Закрыть без сохранения изменений?")) close(); }}>Отмена</button></div>
  </fieldset></form></section>;
}
function RouteBookmark({ route, saved }: { route: LibraryRoute; saved: (route: LibraryRoute) => void }) {
  const [note, setNote] = useState(route.myNote || ""), [collection, setCollection] = useState(route.collectionId || ""), [collections, setCollections] = useState<RouteCollection[]>([]);
  const [error, setError] = useState(""), [collectionsError, setCollectionsError] = useState(""), [retry, setRetry] = useState(0), [busy, setBusy] = useState(false);
  useEffect(() => { let active = true; setCollectionsError(""); api<RouteCollection[]>("/route-collections").then(c => { if (active) setCollections(c); }).catch(e => { if (active) setCollectionsError(e.message); }); return () => { active = false; }; }, [retry]);
  return <section className="profile-card group-editor"><h2>{route.isSaved ? "Сохранённый маршрут" : "Сохранить для себя"}</h2><p className="form-hint">Заметка и подборка видны только вам.</p>
    {collectionsError && <ErrorBox message={collectionsError} retry={() => setRetry(v => v + 1)} />}
    <form onSubmit={async e => { e.preventDefault(); if (busy) return; setBusy(true); setError(""); try { saved(await api<LibraryRoute>(`/route-library/${route.id}/bookmark`, { method: "PUT", body: JSON.stringify({ note, collectionId: collection || null }) })); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>
      <label>Личная заметка<textarea maxLength={2000} value={note} disabled={busy} onChange={e => setNote(e.target.value)} /></label><label>Подборка<select value={collection} disabled={busy || !!collectionsError} onChange={e => setCollection(e.target.value)}><option value="">Без подборки</option>{collections.map(c => <option key={c.id} value={c.id}>{c.name}</option>)}</select></label><a className="text-button" href="#/routes">Управлять подборками</a>
      {error && <ErrorBox message={error} />}<div className="content-tools"><button className="button secondary" disabled={busy}>{busy ? "Сохраняем…" : "Сохранить заметку и маршрут"}</button>{route.isSaved && <button type="button" className="text-button" disabled={busy} onClick={async () => { if (!window.confirm("Убрать из сохранённых и удалить личную заметку?")) return; setBusy(true); setError(""); try { saved(await api<LibraryRoute>(`/route-library/${route.id}/bookmark`, { method: "DELETE" })); setNote(""); setCollection(""); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>Убрать из сохранённых</button>}</div>
    </form>
  </section>;
}
function Reviews({ routeId, me, changed }: { routeId: string; me: User | null; changed: () => void }) {
  const [data, setData] = useState<Page<RouteReview> | null>(null), [page, setPage] = useState(1), [revision, setRevision] = useState(0), [error, setError] = useState(""), [busy, setBusy] = useState(false);
  const timeZone = Intl.DateTimeFormat().resolvedOptions().timeZone;
  const today = rideLocalTime(new Date().toISOString(), timeZone).slice(0, 10);
  const [rating, setRating] = useState(5), [text, setText] = useState(""), [date, setDate] = useState(today);
  useEffect(() => { let active = true; setData(null); setError(""); api<Page<RouteReview>>(`/route-library/${routeId}/reviews?page=${page}&pageSize=20`).then(r => { if (active) setData(r); }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [routeId, page, revision]);
  return <section><h2>После поездки</h2>{error && <ErrorBox message={error} retry={() => setRevision(v => v + 1)} />}{!data ? !error && <Loading /> : <>{data.items.map(review => <article className="profile-card" key={review.id}><div className="content-tools"><Avatar user={review.author} small /><a href={userHref(review.author)}>{review.author.name}</a><span>★ {review.rating} · {review.riddenOn}</span></div><p className="group-content"><MentionText text={review.text} /></p><div className="content-tools">
    {me?.id === review.author.id && <button className="text-button" disabled={busy} onClick={() => { setRating(review.rating); setText(review.text); setDate(review.riddenOn); }}>Изменить отзыв</button>}
    {(me?.id === review.author.id || me?.isModerator) && <button className="text-button" disabled={busy} onClick={async () => { if (!window.confirm("Удалить отзыв?")) return; setBusy(true); try { await api(`/route-library/${routeId}/reviews/${review.id}`, { method: "DELETE" }); setRevision(v => v + 1); changed(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>Удалить отзыв</button>}
    {me && me.id !== review.author.id && <ReportButton type={12} id={review.id} notify={setError} />}</div></article>)}
    {!data.items.length && <p className="muted">Проехали маршрут? Поделитесь состоянием дороги и впечатлениями.</p>}
    {(page > 1 || data.hasMore) && <div className="content-tools"><button className="text-button" disabled={page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница {page}</span><button className="text-button" disabled={!data.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}</>}
    {me ? <form className="profile-card group-editor" onSubmit={async e => { e.preventDefault(); if (busy) return; setBusy(true); setError(""); try { await api(`/route-library/${routeId}/reviews/me`, { method: "PUT", body: JSON.stringify({ rating, text, riddenOn: date, timeZone }) }); setText(""); setPage(1); setRevision(v => v + 1); changed(); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>
      <h3>Мой отзыв после поездки</h3><p className="form-hint">Один отзыв на маршрут; повторное сохранение обновляет ваш отзыв.</p><label>Оценка<select value={rating} disabled={busy} onChange={e => setRating(Number(e.target.value))}>{[5, 4, 3, 2, 1].map(n => <option key={n} value={n}>{n} из 5</option>)}</select></label><label>Дата поездки<input type="date" required min="1900-01-01" max={today} value={date} disabled={busy} onChange={e => setDate(e.target.value)} /></label><label>Впечатления<textarea required maxLength={2000} value={text} disabled={busy} onChange={e => setText(e.target.value)} /></label><button className="button secondary" disabled={busy || !text.trim()}>Сохранить отзыв</button>
    </form> : <button className="button secondary" onClick={requestLogin}>Войти и оставить отзыв</button>}
  </section>;
}
export function LibraryRouteDetail({ id, me, source }: { id: string; me: User | null; source?: { kind: "post" | "entry" | "ride"; id: string } | null }) {
  const isNew = id === "new";
  const [route, setRoute] = useState<LibraryRoute | null>(null), [initialRoute, setInitialRoute] = useState<RouteData | null>(null), [sourceReady, setSourceReady] = useState(!source), [editing, setEditing] = useState(false);
  const [error, setError] = useState(""), [retry, setRetry] = useState(0), [busy, setBusy] = useState(false);
  useEffect(() => { let active = true; setError(""); if (isNew && source) {
    setSourceReady(false); const resource = { post: "posts", entry: "group-entries", ride: "rides" }[source.kind];
    api<{ route: RouteData | null }>(`/${resource}/${encodeURIComponent(source.id)}`).then(value => { if (active) { if (!value.route) throw Error("В исходном материале нет маршрута."); setInitialRoute(value.route); setSourceReady(true); } }).catch(e => { if (active) setError(e.message); });
  } else if (!isNew) api<LibraryRoute>(`/route-library/${id}`).then(r => { if (active) setRoute(r); }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [id, isNew, source?.kind, source?.id, retry]);
  const saved = (r: LibraryRoute) => { setRoute(r); setEditing(false); if (isNew) window.location.hash = `/route/${r.id}`; };
  if (isNew) {
    if (!me) return <Empty title="Поделитесь маршрутом" description="Войдите для публикации." action={<button className="button primary" onClick={requestLogin}>Войти</button>} />;
    if (error) return <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />;
    if (!sourceReady) return <Loading />;
    return <><a className="back-link" href="#/routes">← Библиотека маршрутов</a><LibraryEditor initialRoute={initialRoute} saved={saved} close={() => { window.location.hash = "/routes"; }} /></>;
  }
  if (!route) return error ? <ErrorBox message={error} retry={() => setRetry(v => v + 1)} /> : <Loading />;
  const manage = me?.id === route.author.id || me?.isModerator;
  return <><a className="back-link" href="#/routes">← Все маршруты</a><div className="page-heading"><div className="eyebrow">{route.city} · {surfaces[route.surface]}</div><h1>{route.title}</h1><p>{route.distanceKm.toLocaleString("ru-RU")} км · {route.rating ? `★ ${route.rating.toFixed(1)}` : "Без оценок"} · {route.reviewsCount} отзывов</p></div>
    {error && <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />}<div className="content-tools"><Avatar user={route.author} /><a href={userHref(route.author)}>{route.author.name}</a></div><p className="group-content"><MentionText text={route.description} /></p><PostRoute route={route.route} />
    <div className="content-tools">{manage && <><button className="text-button" disabled={busy} onClick={() => setEditing(v => !v)}>Изменить маршрут</button><button className="text-button" disabled={busy} onClick={async () => { if (!window.confirm("Удалить маршрут из библиотеки?")) return; setBusy(true); setError(""); try { await api(`/route-library/${id}`, { method: "DELETE" }); window.location.hash = "/routes"; } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>Удалить маршрут</button></>}{me && <ReportButton type={11} id={id} notify={setError} />}</div>
    {editing && manage && <LibraryEditor key={`${route.id}-${route.revision}-${me!.id}`} value={route} saved={saved} close={() => setEditing(false)} />}
    {me ? <RouteBookmark key={`${id}-${me.id}-${route.isSaved}`} route={route} saved={setRoute} /> : <button className="button secondary" onClick={requestLogin}>Войти и сохранить маршрут</button>}
    <Reviews key={`${id}-${me?.id || "guest"}`} routeId={id} me={me} changed={() => setRetry(v => v + 1)} />
  </>;
}
