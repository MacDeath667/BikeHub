import { useEffect, useRef, useState } from "react";
import { api, relativeTime, requestLogin, userHref } from "../api";
import type { Conversation, MessagePeer, Page, User } from "../api";
import { Avatar, Empty, ErrorBox, Loading } from "../components/ui";
import { MentionTextarea } from "../components/MentionTextarea";
import { messagesChanged, notifyMessagesChanged, prepareMessage } from "../components/messageState";
import type { PendingMessage } from "../components/messageState";
import { Archive, Inbox as InboxIcon, MessageCircle, ShieldCheck } from "lucide-react";

function conversationStatus(conversation: Conversation) {
  if (conversation.blockedByMe) { return "Заблокирована"; }
  if (!conversation.peer.available) { return "Недоступна"; }
  if (conversation.status === 1) { return conversation.requestedByMe ? "Ожидает согласия" : "Новый запрос"; }
  if (conversation.status === 3) { return "Запрос отклонён"; }
  return conversation.canSend ? "Переписка" : "Недоступна";
}

export function PeerName({ peer }: { peer: MessagePeer }) {
  return peer.available && peer.handle ? <a className="message-peer-name" href={`#/u/${encodeURIComponent(peer.handle)}`}><strong>{peer.name}</strong><span>@{peer.handle}</span></a> : <strong className="message-peer-name">{peer.name}</strong>;
}
export function PeerAvatar({ peer }: { peer: MessagePeer }) {
  return <span className="notification-avatar">{peer.avatarUrl ? <img src={peer.avatarUrl} alt="" /> : peer.name.slice(0, 1)}</span>;
}
export function MessageCount({ userId }: { userId: string }) {
  const [count, setCount] = useState(0);
  useEffect(() => { let active = true, pending = false; setCount(0); const load = async () => {
    if (document.hidden || pending) return; pending = true;
    try { const next = await api<number>("/messages/unread-count"); if (active) setCount(next); } catch { /* Keep the last known count. */ } finally { pending = false; }
  }; void load(); const timer = window.setInterval(() => void load(), 60000); window.addEventListener(messagesChanged, load); window.addEventListener("focus", load);
    return () => { active = false; window.clearInterval(timer); window.removeEventListener(messagesChanged, load); window.removeEventListener("focus", load); };
  }, [userId]);
  return count > 0 ? <span className="count" aria-label={`Непрочитанных сообщений: ${count}`}>{count > 99 ? "99+" : count}</span> : null;
}
function BlockedContacts() {
  const [data, setData] = useState<Page<User> | null>(null), [page, setPage] = useState(1), [revision, setRevision] = useState(0), [error, setError] = useState(""), [busy, setBusy] = useState(false);
  useEffect(() => { let active = true; setData(null); setError(""); api<Page<User>>(`/messages/blocks?page=${page}&pageSize=20`).then(d => { if (active) setData(d); }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [page, revision]);
  return <>{error && <ErrorBox message={error} retry={() => setRevision(v => v + 1)} />}{!data ? !error && <Loading /> : <>
    {!data.items.length && <Empty title="Заблокированных контактов нет" description="Блокировка сообщений доступна в диалоге." />}
    {data.items.map(user => <section className="profile-card blocked-contact" key={user.id}><Avatar user={user} small /><a className="message-peer-name" href={userHref(user)}><strong>{user.name}</strong><span>@{user.handle}</span></a><div className="content-tools"><a className="text-button" href={`#/messages?to=${user.id}`}>Переписка</a><button className="button secondary" disabled={busy} onClick={async () => { if (!window.confirm("Разрешить сообщения от этого пользователя?")) return; setBusy(true); setError(""); try { await api(`/messages/blocks/${user.id}`, { method: "DELETE" }); notifyMessagesChanged(); setRevision(v => v + 1); } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }}>Разблокировать сообщения</button></div></section>)}
    {(page > 1 || data.hasMore) && <div className="content-tools"><button className="text-button" disabled={busy || page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница {page}</span><button className="text-button" disabled={busy || !data.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}</>}</>;
}
function StartConversation({ userId }: { userId: string }) {
  const [target, setTarget] = useState<{ peer: MessagePeer; conversationId: string | null; canRequest: boolean; blockedByMe: boolean } | null>(null);
  const [text, setText] = useState(""), [error, setError] = useState(""), [retry, setRetry] = useState(0), [busy, setBusy] = useState(false), [existing, setExisting] = useState<string | null>(null);
  const pending = useRef<PendingMessage | null>(null), submitting = useRef(false);
  useEffect(() => { let active = true; setTarget(null); setError(""); api<NonNullable<typeof target>>(`/messages/peers/${encodeURIComponent(userId)}`).then(value => {
    if (active) { if (value.conversationId) window.location.hash = `/message/${value.conversationId}`; else setTarget(value); }
  }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [userId, retry]);
  if (!target) return error ? <ErrorBox message={error} retry={() => setRetry(v => v + 1)} /> : <Loading />;
  return <section className="messaging-page"><a className="back-link" href="#/messages">← Сообщения</a><div className="page-heading"><h1>Написать участнику</h1><div className="message-recipient"><PeerAvatar peer={target.peer} /><PeerName peer={target.peer} /></div><p>Отправьте одно сообщение с причиной обращения. Продолжить можно после согласия собеседника.</p></div>
    {error && <ErrorBox message={error} />}{existing && <a className="button secondary" href={`#/message/${existing}`}>Открыть существующую переписку</a>}
    {target.canRequest ? <form className="profile-card group-editor" onSubmit={async e => { e.preventDefault(); if (submitting.current || !text.trim() || existing) return; submitting.current = true; setBusy(true); setError(""); const body = prepareMessage(pending.current, text); pending.current = body;
      try { const conversation = await api<Conversation>("/messages/conversations", { method: "POST", body: JSON.stringify({ userId, ...body }) }); pending.current = null; notifyMessagesChanged(); window.location.hash = `/message/${conversation.id}`; }
      catch (e) { setError((e as Error).message); try { const value = await api<NonNullable<typeof target>>(`/messages/peers/${encodeURIComponent(userId)}`); setExisting(value.conversationId); } catch { /* Preserve the original sending error and draft. */ } }
      finally { submitting.current = false; setBusy(false); }
    }}><label>Первое сообщение<MentionTextarea required maxLength={4000} value={text} disabled={busy} onValueChange={setText} /></label><button className="button primary" disabled={busy || !text.trim() || !!existing}>{busy ? "Отправляем…" : "Отправить запрос"}</button></form> : <Empty title="Переписка сейчас недоступна" description={target.blockedByMe ? "Вы заблокировали сообщения этого пользователя. Управляйте блокировками в разделе сообщений." : "Отправить запрос этому пользователю сейчас нельзя."} />}
  </section>;
}
export function Messages({ me, toUserId }: { me: User | null; toUserId?: string | null }) {
  return !me ? <Empty title="Личные сообщения" description="Войдите, чтобы общаться с другими участниками." action={<button className="button primary" onClick={requestLogin}>Войти</button>} /> : toUserId ? <StartConversation key={`${me.id}-${toUserId}`} userId={toUserId} /> : <Inbox />;
}
function Inbox() {
  const [tab, setTab] = useState("inbox"), [page, setPage] = useState(1), [revision, setRevision] = useState(0), [data, setData] = useState<Page<Conversation> | null>(null), [error, setError] = useState("");
  useEffect(() => { let active = true, pending = false; setData(null); setError(""); if (tab === "blocks") return;
    const load = async () => { if (pending) return; pending = true; try { const result = await api<Page<Conversation>>(`/messages/conversations?requests=${tab === "requests"}&archived=${tab === "archive"}&page=${page}&pageSize=20`); if (active) { setData(result); setError(""); } } catch (e) { if (active) setError((e as Error).message); } finally { pending = false; } };
    void load(); const timer = window.setInterval(() => { if (!document.hidden) void load(); }, 30000); const focus = () => { if (!document.hidden) void load(); }; window.addEventListener("focus", focus);
    return () => { active = false; window.clearInterval(timer); window.removeEventListener("focus", focus); };
  }, [tab, page, revision]);
  return <section className="messaging-page"><div className="page-heading"><h1>Личные сообщения</h1></div>
    <div className="content-tools message-tabs" role="group" aria-label="Раздел сообщений">{[{ id: "inbox", name: "Диалоги", icon: MessageCircle }, { id: "requests", name: "Запросы", icon: InboxIcon }, { id: "archive", name: "Архив", icon: Archive }, { id: "blocks", name: "Блокировки", icon: ShieldCheck }].map(item => <button className={`button ${tab === item.id ? "primary" : "secondary"}`} key={item.id} aria-pressed={tab === item.id} onClick={() => { setTab(item.id); setPage(1); }}><item.icon size={17} aria-hidden="true" />{item.name}</button>)}</div>
    {tab === "blocks" ? <BlockedContacts /> : <>{error && <ErrorBox message={error} retry={() => setRevision(v => v + 1)} />}{!data ? !error && <Loading /> : <>
      {!!data.items.length && <div className="conversation-list">{data.items.map(conversation => <a className={`conversation-card${conversation.unreadCount ? " unread" : ""}`} href={`#/message/${conversation.id}`} key={conversation.id}>
        <PeerAvatar peer={conversation.peer} /><div className="conversation-card-body">
          <div className="conversation-card-title"><strong>{conversation.peer.name}</strong>{conversation.peer.handle && <small>@{conversation.peer.handle}</small>}{conversation.unreadCount > 0 && <span className="count" aria-label={`Непрочитанных: ${conversation.unreadCount}`}>{conversation.unreadCount}</span>}</div>
          <div className="conversation-card-preview"><p>{conversation.lastText || "Сообщение недоступно"}</p><span className="conversation-request-badge">{conversationStatus(conversation)}</span></div>
          <time className="sr-only" dateTime={conversation.updatedAt}>{relativeTime(conversation.updatedAt)}</time>
        </div>
      </a>)}</div>}
      {!data.items.length && <Empty title={tab === "requests" ? "Новых запросов нет" : tab === "archive" ? "Архив пуст" : "Здесь появятся ваши диалоги"} description="Переписка доступна только её участникам." />}
      {(page > 1 || data.hasMore) && <div className="content-tools"><button className="text-button" disabled={page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница {page}</span><button className="text-button" disabled={!data.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}</>}</>}
  </section>;
}
