import { userHref } from "../api";
import { RouteCover } from "../components/RouteCover";
import { PhotoUpload } from "../components/PhotoUpload";
import { CommunityAvatar } from "../components/CommunityAvatar";
import { CommunityMembers } from "../components/CommunityMembers";
import { PostRoute, RouteEditor } from "../components/PostRoute";
import type { RouteData } from "../api";
import { useEffect, useState } from "react";
import { api, categories, relativeTime, requestLogin } from "../api";
import type { Community, User, Page, Photo, Comment } from "../api";
import { ErrorBox, Loading, Empty, Avatar } from "../components/ui";
import { PhotoCollection } from "../components/PhotoCollection";
import { PhotoGallery } from "../components/PhotoGallery";
import { ReportButton } from "../components/ContentTools";

type Entry = { hasRoute?: boolean; route?: RouteData | null; id: string; groupId: string; groupName: string; author: User; kind: number; title: string; content: string; photos: Photo[]; imageIds: string[]; createdAt: string; repliesCount: number };
export function GroupEditor({ group, saved, close }: { group?: Community; saved: (g: Community) => void; close: () => void }) {
  const [imageId, setImageId] = useState<string | null>(group?.imageId || null);
  const [uploading, setUploading] = useState(false);
  const [name, setName] = useState(group?.name || "");
  const [description, setDescription] = useState(group?.description || "");
  const [category, setCategory] = useState(group?.category || "gravel");
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  return <section className="profile-card group-editor"><h2>{group ? "Настройки сообщества" : "Новое сообщество"}</h2>
    <form onSubmit={async e => { e.preventDefault(); if (busy || uploading) return; setBusy(true); setError(""); try {
      saved(await api<Community>(`/communities${group ? `/${group.id}` : ""}`, { method: group ? "PUT" : "POST", body: JSON.stringify({ name, description, category, imageId }) }));
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>
      <PhotoUpload avatar label="Аватарка сообщества" value={group?.imageId ? group.imageUrl : null} onChange={setImageId} onBusy={setUploading} disabled={busy} />
      <label>Название<input required minLength={2} maxLength={100} value={name} onChange={e => setName(e.target.value)} /></label>
      <label>О сообществе<textarea required maxLength={2000} value={description} onChange={e => setDescription(e.target.value)} /></label>
      <label>Направление<select value={category} onChange={e => setCategory(e.target.value)}>{Object.entries(categories).map(([key, value]) => <option key={key} value={key}>{value}</option>)}</select></label>
      {error && <ErrorBox message={error} />}<div className="content-tools"><button className="button primary" disabled={busy || uploading}>Сохранить</button><button type="button" className="text-button" disabled={busy || uploading} onClick={close}>Отмена</button></div>
    </form></section>;
}
function EntryEditor({ groupId, kind, entry, saved, close }: { groupId: string; kind: number; entry?: Entry; saved: (e: Entry) => void; close: () => void }) {
  const [title, setTitle] = useState(entry?.title || ""), [content, setContent] = useState(entry?.content || "");
  const [route, setRoute] = useState<RouteData | null>(entry?.route || null);
  const [routeBusy, setRouteBusy] = useState(false);
  const [photos, setPhotos] = useState<Photo[]>(entry?.photos || []);
  const [uploading, setUploading] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState("");
  return <section className="profile-card group-editor"><h2>{entry ? "Редактирование" : kind === 1 ? "Новый пост" : "Новое обсуждение"}</h2><form onSubmit={async e => {
    e.preventDefault(); if (busy || uploading || routeBusy) return; setBusy(true); setError(""); try {
      saved(await api<Entry>(`/communities/${groupId}/entries${entry ? `/${entry.id}` : ""}`, { method: entry ? "PUT" : "POST", body: JSON.stringify({ kind, title, content, imageIds: photos.map(p => p.id), route }) }));
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }}>
    <label>Заголовок<input required minLength={2} maxLength={160} value={title} onChange={e => setTitle(e.target.value)} /></label>
    <label>{kind === 1 ? "Текст поста" : "Что обсудим?"}<textarea required maxLength={20000} rows={7} value={content} onChange={e => setContent(e.target.value)} /></label>
    <PhotoCollection photos={photos} onChange={setPhotos} onBusy={setUploading} disabled={busy} />
    <RouteEditor value={route} onChange={setRoute} onBusy={setRouteBusy} disabled={busy || uploading} />
    {error && <ErrorBox message={error} />}<div className="content-tools"><button className="button primary" disabled={busy || uploading || routeBusy}>{entry ? "Сохранить" : "Опубликовать"}</button><button type="button" className="text-button" onClick={close}>Отмена</button></div>
  </form></section>;
}
export function CommunityDetail({ id, me, reload }: { id: string; me: User | null; reload: () => void }) {
  const [showMembers, setShowMembers] = useState(false);
  const [group, setGroup] = useState<Community | null>(null), [entries, setEntries] = useState<Page<Entry> | null>(null);
  const [kind, setKind] = useState(1), [page, setPage] = useState(1), [revision, setRevision] = useState(0);
  const [editing, setEditing] = useState(false), [creating, setCreating] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState("");
  const refresh = () => { setRevision(v => v + 1); reload(); };
  useEffect(() => { let active = true; setEntries(null); Promise.all([api<Community>(`/communities/${id}`), api<Page<Entry>>(`/communities/${id}/entries?kind=${kind}&page=${page}`)])
    .then(([g, e]) => { if (active) { setGroup(g); setEntries(e); setError(""); } }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [id, kind, page, revision, me?.id]);
  if (!group) return error ? <ErrorBox message={error} /> : <Loading />;
  return <><a className="back-link" href="#/communities">← Все сообщества</a>{showMembers && <CommunityMembers community={group} close={() => setShowMembers(false)} />}<div className="page-heading community-heading"><CommunityAvatar community={group} large /><div><div className="eyebrow">{categories[group.category]} · <button type="button" className="community-members-button" onClick={() => setShowMembers(true)} aria-label={`Участники сообщества ${group.name}: ${group.members}`}>{group.members} участников</button></div><h1>{group.name}</h1><p>{group.description}</p></div></div>
    <div className="content-tools"><button className={`button ${group.isJoined ? "secondary" : "primary"}`} disabled={busy} onClick={async () => { if (!me) { requestLogin(); return; } setBusy(true); try {
      await api(`/communities/${id}/membership`, { method: group.isJoined ? "DELETE" : "PUT" }); refresh();
    } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>{group.isJoined ? "Выйти из сообщества" : "Вступить"}</button>
      {(group.ownerId === me?.id || me?.isModerator) && <button className="text-button" onClick={() => setEditing(!editing)}>Настройки</button>}
      {me && <ReportButton type={6} id={id} notify={setError} />}</div>
    {editing && <GroupEditor group={group} close={() => setEditing(false)} saved={() => { setEditing(false); refresh(); }} />}
    <div className="content-tools group-tabs">{[1, 2].map(k => <button key={k} className={`button ${kind === k ? "primary" : "secondary"}`} onClick={() => { setKind(k); setPage(1); setCreating(false); }}>{k === 1 ? "Посты" : "Обсуждения"}</button>)}
      {group.isJoined && <button className="text-button" onClick={() => setCreating(!creating)}>{kind === 1 ? "Написать пост" : "Начать обсуждение"}</button>}</div>
    {creating && <EntryEditor groupId={id} kind={kind} close={() => setCreating(false)} saved={e => { window.location.hash = `/group-entry/${e.id}`; }} />}
    {error && <ErrorBox message={error} />}{!entries ? <Loading /> : entries.items.length ? entries.items.map(e => <article className="profile-card group-entry-card" key={e.id}><div className="content-tools"><Avatar user={e.author} /><a href={userHref(e.author)}>{e.author.name}</a><span className="muted">{relativeTime(e.createdAt)}</span></div>
      <a href={`#/group-entry/${e.id}`}><h2>{e.title}</h2></a><p>{e.content.slice(0, 280)}{e.content.length > 280 ? "…" : ""}</p>{!e.photos.length && e.hasRoute && <RouteCover previewUrl={`/group-entries/${e.id}/route-preview`} href={`#/group-entry/${e.id}`} />}{e.photos[0] && <a href={`#/group-entry/${e.id}`}><img className="group-entry-cover" src={e.photos[0].imageUrl} alt="Фотография к записи" /></a>}<a className="text-button" href={`#/group-entry/${e.id}`}>{e.repliesCount} ответов →</a></article>) : <Empty title={kind === 1 ? "Здесь ещё нет постов" : "Обсуждения пока не начались"} description="Поделитесь первым — участники присоединятся." />}
    {entries && <div className="content-tools"><button className="text-button" disabled={page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница {page}</span><button className="text-button" disabled={!entries.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}</>;
}
export function GroupEntryDetail({ id, me }: { id: string; me: User | null }) {
  const [entry, setEntry] = useState<Entry | null>(null), [group, setGroup] = useState<Community | null>(null), [replies, setReplies] = useState<Page<Comment> | null>(null);
  const [page, setPage] = useState(1), [revision, setRevision] = useState(0), [text, setText] = useState(""), [editing, setEditing] = useState(false), [busy, setBusy] = useState(false), [error, setError] = useState("");
  useEffect(() => { let active = true; Promise.all([api<Entry>(`/group-entries/${id}`), api<Page<Comment>>(`/group-entries/${id}/replies?page=${page}`)]).then(async ([e, r]) => {
    const g = await api<Community>(`/communities/${e.groupId}`); if (active) { setEntry(e); setReplies(r); setGroup(g); setError(""); }
  }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [id, page, revision, me?.id]);
  async function remove(path: string, whole = false) { if (busy || !window.confirm("Удалить материал?")) return; setBusy(true); try { await api(path, { method: "DELETE" }); if (whole) window.location.hash = `/community/${entry!.groupId}`; else setRevision(v => v + 1); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }
  if (!entry || !group) return error ? <ErrorBox message={error} /> : <Loading />;
  const manager = group.ownerId === me?.id || me?.isModerator;
  return <><a className="back-link" href={`#/community/${entry.groupId}`}>← {entry.groupName}</a><div className="eyebrow">{entry.kind === 1 ? "ПОСТ" : "ОБСУЖДЕНИЕ"}</div><h1>{entry.title}</h1><div className="content-tools"><Avatar user={entry.author} /><a href={userHref(entry.author)}>{entry.author.name}</a><span>{relativeTime(entry.createdAt)}</span></div>
    <p className="group-content">{entry.content}</p>{!entry.photos.length && entry.route ? <RouteCover route={entry.route} /> : entry.photos.length > 0 && <PhotoGallery photos={entry.photos} fallback={entry.photos[0].imageUrl} title={entry.title} />}
    <PostRoute route={entry.route} showMap={!!entry.photos.length} />
    <div className="content-tools">{entry.author.id === me?.id && <button className="text-button" onClick={() => setEditing(!editing)}>Редактировать</button>}{(entry.author.id === me?.id || manager) && <button className="text-button" disabled={busy} onClick={() => remove(`/group-entries/${id}`, true)}>Удалить</button>}{me && <ReportButton type={7} id={id} notify={setError} />}</div>
    {editing && <EntryEditor groupId={entry.groupId} kind={entry.kind} entry={entry} close={() => setEditing(false)} saved={() => { setEditing(false); setRevision(v => v + 1); }} />}
    {error && <ErrorBox message={error} />}<h2>Ответы · {entry.repliesCount}</h2>
    {replies?.items.map(r => <section className="profile-card group-entry-card" key={r.id}><div className="content-tools"><Avatar user={r.author} /><a href={userHref(r.author)}>{r.author.name}</a><small>{relativeTime(r.createdAt)}</small></div><p className="group-content">{r.text}</p><div className="content-tools">{(me?.id === r.author.id || manager) && <button className="text-button" disabled={busy} onClick={() => remove(`/group-entries/${id}/replies/${r.id}`)}>Удалить ответ</button>}{me && <ReportButton type={8} id={r.id} notify={setError} />}</div></section>)}
    {replies && <div className="content-tools"><button className="text-button" disabled={page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница {page}</span><button className="text-button" disabled={!replies.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}
    {group.isJoined ? <form className="group-editor" onSubmit={async e => { e.preventDefault(); if (busy) return; setBusy(true); try { await api(`/group-entries/${id}/replies`, { method: "POST", body: JSON.stringify({ text }) }); setText(""); setPage(Math.max(1, Math.ceil((entry.repliesCount + 1) / 50))); setRevision(v => v + 1); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}><label>Ваш ответ<textarea required maxLength={2000} value={text} onChange={e => setText(e.target.value)} /></label><button className="button primary" disabled={busy || !text.trim()}>Отправить</button></form> : <p><a href={`#/community/${group.id}`}>Вступите в сообщество</a>, чтобы ответить.</p>}</>;
}
