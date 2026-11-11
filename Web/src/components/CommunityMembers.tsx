import { userHref } from "../api";
import { useEffect, useRef, useState } from "react";
import { createPortal } from "react-dom";
import { X } from "lucide-react";
import { api, number } from "../api";
import type { Community, Page, User } from "../api";
import { Avatar, Empty, ErrorBox, Loading } from "./ui";

export function CommunityMembers({ community, close }: { community: Community; close: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [page, setPage] = useState(1), [revision, setRevision] = useState(0);
  const [members, setMembers] = useState<Page<User> | null>(null), [error, setError] = useState("");
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => element?.close(); }, []);
  useEffect(() => {
    let active = true; setMembers(null); setError("");
    api<Page<User>>(`/communities/${community.id}/members?page=${page}&pageSize=20`).then(result => { if (active) setMembers(result); })
      .catch(error => { if (active) setError(error.message); });
    return () => { active = false; };
  }, [community.id, page, revision]);
  return createPortal(<dialog ref={dialog} className="auth-dialog community-members-dialog" aria-labelledby="community-members-title" onCancel={event => { event.preventDefault(); close(); }} onClick={event => {
    if (event.target !== event.currentTarget) return;
    const bounds = event.currentTarget.getBoundingClientRect();
    if (event.clientX < bounds.left || event.clientX > bounds.right || event.clientY < bounds.top || event.clientY > bounds.bottom) close();
  }}>
    <div className="dialog-header"><div><h2 id="community-members-title">Участники</h2><p className="muted">{community.name}{members ? ` · ${number(members.total)}` : ""}</p></div><button type="button" className="icon-button" aria-label="Закрыть список участников" onClick={close}><X size={20} /></button></div>
    {error ? <ErrorBox message={error} retry={() => setRevision(v => v + 1)} /> : !members ? <Loading /> : !members.items.length ? <Empty title="Пока нет участников" description="Участники сообщества появятся здесь." /> : <ul className="community-members-list">{members.items.map(user => <li key={user.id}><a href={userHref(user)} onClick={close}><Avatar user={user} /><span><strong>{user.name}</strong><small>{user.handle ? `@${user.handle}` : "Ник ещё не выбран"}{user.city ? ` · ${user.city}` : ""}</small></span>{user.id === community.ownerId && <span className="community-owner-label">Создатель</span>}</a></li>)}</ul>}
    {members && members.total > members.pageSize && <div className="content-tools"><button type="button" className="text-button" disabled={page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница {page}</span><button type="button" className="text-button" disabled={!members.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}
  </dialog>, document.body);
}
