import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { api, relativeTime, requestLogin } from "../api";
import type { Conversation as ConversationData, DirectMessage, MessagePage, MessagePeer, User } from "../api";
import { Empty, ErrorBox, Loading } from "../components/ui";
import { MentionText } from "../components/MentionText";
import { MentionTextarea } from "../components/MentionTextarea";
import { mergeMessages, notifyMessagesChanged, prepareMessage } from "../components/messageState";
import type { PendingMessage } from "../components/messageState";
import { PeerAvatar, PeerName } from "./Messages";

export function Conversation({ id, me }: { id: string; me: User | null }) {
  return me ? <ConversationPage id={id} me={me} /> : <Empty title="Личная переписка" description="Войдите для просмотра своих сообщений." action={<button className="button primary" onClick={requestLogin}>Войти</button>} />;
}
function ConversationPage({ id, me }: { id: string; me: User }) {
  const base = `/messages/conversations/${id}`;
  const [conversation, setConversation] = useState<ConversationData | null>(null), [messages, setMessages] = useState<DirectMessage[]>([]);
  const [error, setError] = useState(""), [syncError, setSyncError] = useState(""), [sendError, setSendError] = useState(""), [actionError, setActionError] = useState(""), [info, setInfo] = useState("");
  const [retry, setRetry] = useState(0), [hasEarlier, setHasEarlier] = useState(false), [historyBusy, setHistoryBusy] = useState(false), [atBottom, setAtBottom] = useState(true);
  const [text, setText] = useState(""), [sending, setSending] = useState(false), [acting, setActing] = useState(false), [reportId, setReportId] = useState<string | null>(null), [reason, setReason] = useState("");
  const pendingSend = useRef<PendingMessage | null>(null), submitting = useRef(false), actionPending = useRef(false), loadingHistory = useRef(false);
  const cursor = useRef(0), oldest = useRef(0), list = useRef<HTMLDivElement>(null), alive = useRef(true);
  const readSent = useRef(0), reading = useRef(false), refresh = useRef<() => Promise<void>>(async () => {});
  const removedPeer = useRef<MessagePeer | null>(null);
  function currentConversation(details: ConversationData): ConversationData {
    if (!details.peer.id) removedPeer.current = details.peer;
    return removedPeer.current ? { ...details, peer: removedPeer.current, canSend: false, canAccept: false } : details;
  }
  const visibleMessages = (items: DirectMessage[]) => removedPeer.current ? items.filter(message => message.senderId === me.id) : items;
  useEffect(() => {
    let active = true, initialized = false, pending = false; alive.current = true; setConversation(null); setMessages([]); setError(""); setSyncError(""); readSent.current = 0; cursor.current = 0; oldest.current = 0;
    const initialize = async () => {
      try {
        const [details, page] = await Promise.all([api<ConversationData>(base), api<MessagePage>(`${base}/messages?pageSize=50`)]);
        if (active) { setConversation(currentConversation(details)); setMessages(visibleMessages(page.items)); setHasEarlier(page.hasMoreBefore); cursor.current = page.latestSequence; oldest.current = page.items[0]?.sequence || 0; initialized = true; }
      } catch (e) { if (active) setError((e as Error).message); }
    };
    const poll = async () => {
      if (!active || !initialized || pending || loadingHistory.current || document.hidden) return; pending = true;
      try {
        const [details, next, recent] = await Promise.all([api<ConversationData>(base), api<MessagePage>(`${base}/messages?afterSequence=${cursor.current}&pageSize=50`), api<MessagePage>(`${base}/messages?pageSize=50`)]);
        if (active) {
          cursor.current = Math.max(cursor.current, next.items.length ? next.items[next.items.length - 1].sequence : next.latestSequence);
          setConversation(currentConversation(details)); setMessages(previous => visibleMessages(mergeMessages(previous, recent.items, next.items))); setSyncError("");
          if (next.hasMoreAfter) window.setTimeout(() => void poll(), 100);
        }
      } catch (e) { if (active) setSyncError((e as Error).message); } finally { pending = false; }
    };
    refresh.current = poll; void initialize(); const timer = window.setInterval(() => void poll(), 15000);
    window.addEventListener("focus", poll); document.addEventListener("visibilitychange", poll);
    return () => { active = false; alive.current = false; window.clearInterval(timer); window.removeEventListener("focus", poll); document.removeEventListener("visibilitychange", poll); };
  }, [base, retry]);
  useEffect(() => {
    if (!conversation || !atBottom) return;
    if (list.current) list.current.scrollTop = list.current.scrollHeight;
    const sequence = cursor.current;
    if (document.hidden || reading.current || sequence <= readSent.current) return;
    const previous = readSent.current; readSent.current = sequence; reading.current = true;
    api(`${base}/read`, { method: "PUT", body: JSON.stringify({ sequence }) }).then(() => { if (alive.current) notifyMessagesChanged(); })
      .catch(e => { if (alive.current) { readSent.current = previous; setSyncError((e as Error).message); } }).finally(() => { reading.current = false; });
  }, [messages, atBottom, conversation?.id, base]);
  async function older() {
    if (loadingHistory.current || !oldest.current) return; loadingHistory.current = true; setHistoryBusy(true); setActionError("");
    const height = list.current?.scrollHeight || 0, top = list.current?.scrollTop || 0;
    setAtBottom(false);
    try {
      const page = await api<MessagePage>(`${base}/messages?beforeSequence=${oldest.current}&pageSize=50`);
      if (alive.current) {
        setMessages(previous => visibleMessages(mergeMessages(previous, page.items))); setHasEarlier(page.hasMoreBefore); if (page.items.length) oldest.current = page.items[0].sequence;
        window.setTimeout(() => { if (alive.current && list.current) list.current.scrollTop = top + list.current.scrollHeight - height; }, 0);
      }
    } catch (e) { if (alive.current) setActionError((e as Error).message); } finally { loadingHistory.current = false; if (alive.current) setHistoryBusy(false); }
  }
  async function send(e: FormEvent) {
    e.preventDefault(); if (submitting.current || !text.trim() || !conversation?.canSend) return; submitting.current = true; setSending(true); setSendError(""); setInfo("");
    const body = prepareMessage(pendingSend.current, text); pendingSend.current = body;
    try {
      const message = await api<DirectMessage>(`${base}/messages`, { method: "POST", body: JSON.stringify(body) });
      if (alive.current) {
        setMessages(previous => mergeMessages(previous, [message])); setText(""); pendingSend.current = null;
        setAtBottom(true); if (message.isDeleted) setInfo("Сообщение ранее удалено. Повтор не создал новое сообщение.");
        notifyMessagesChanged();
      }
    } catch (e) { if (alive.current) setSendError((e as Error).message); } finally { submitting.current = false; if (alive.current) setSending(false); }
  }
  async function action(work: () => Promise<unknown>, done?: () => void) {
    if (actionPending.current) return; actionPending.current = true; setActing(true); setActionError(""); setInfo("");
    try {
      await work(); if (!alive.current) return; notifyMessagesChanged(); if (done) done(); else { setConversation(currentConversation(await api<ConversationData>(base))); await refresh.current(); }
    } catch (e) { if (alive.current) setActionError((e as Error).message); } finally { actionPending.current = false; if (alive.current) setActing(false); }
  }
  if (!conversation) return error ? <ErrorBox message={error} retry={() => setRetry(v => v + 1)} /> : <Loading />;
  return <><a className="back-link" href="#/messages">← Все сообщения</a><div className="page-heading message-heading"><div className="content-tools"><PeerAvatar peer={conversation.peer} /><div><div className="eyebrow">ЛИЧНАЯ ПЕРЕПИСКА</div><h1><PeerName peer={conversation.peer} /></h1></div></div></div>
    {conversation.status === 1 && <div className="profile-card" role="status">{conversation.requestedByMe ? "Запрос отправлен. Продолжить можно после согласия собеседника." : "Новый запрос на переписку. Примите его, чтобы ответить."}</div>}
    {conversation.status === 3 && <p role="status">Запрос на переписку отклонён.</p>}
    {conversation.blockedByMe ? <p role="status">Вы заблокировали сообщения этого пользователя.</p> : conversation.status === 2 && !conversation.canSend && <p role="status">Обмен сообщениями сейчас недоступен.</p>}
    <div className="content-tools">{conversation.canAccept && <><button className="button primary" disabled={acting || sending} onClick={() => void action(() => api(`${base}/request`, { method: "PUT", body: JSON.stringify({ accept: true }) }))}>Принять запрос</button><button className="text-button" disabled={acting || sending} onClick={() => void action(() => api(`${base}/request`, { method: "PUT", body: JSON.stringify({ accept: false }) }), () => { window.location.hash = "/messages"; })}>Отклонить запрос</button></>}
      {conversation.status === 1 && conversation.requestedByMe && <button className="text-button" disabled={acting || sending} onClick={() => { if (window.confirm("Отозвать запрос? Повторный запрос этому человеку отправить нельзя до его согласия.")) void action(() => api(`${base}/request`, { method: "DELETE" })); }}>Отозвать запрос</button>}
      <button className="text-button" disabled={acting || sending} onClick={() => void action(() => api(`${base}/archive`, { method: "PUT", body: JSON.stringify({ archived: !conversation.isArchived }) }), () => { window.location.hash = "/messages"; })}>{conversation.isArchived ? "Вернуть из архива" : "В архив"}</button>
      {conversation.peer.id && <button className="text-button" disabled={acting || sending} onClick={() => { if (window.confirm(conversation.blockedByMe ? "Снова разрешить сообщения?" : "Заблокировать сообщения в обе стороны? История сохранится.")) void action(() => api(`/messages/blocks/${conversation.peer.id}`, { method: conversation.blockedByMe ? "DELETE" : "PUT" })); }}>{conversation.blockedByMe ? "Разблокировать сообщения" : "Заблокировать сообщения"}</button>}
    </div>
    {syncError && <ErrorBox message={syncError} retry={() => void refresh.current()} />}{actionError && <ErrorBox message={actionError} />}{info && <p role="status">{info}</p>}
    {hasEarlier && <button className="text-button" disabled={historyBusy} onClick={() => void older()}>{historyBusy ? "Загружаем…" : "Более ранние сообщения"}</button>}
    <div ref={list} className="message-log" role="log" aria-label="Сообщения переписки" aria-live="polite" onScroll={() => { if (list.current) { const bottom = list.current.scrollHeight - list.current.scrollTop - list.current.clientHeight < 80; setAtBottom(bottom); } }}>
      {!messages.length && <p className="muted">В переписке пока нет доступных сообщений.</p>}
      {messages.map(message => <article className={`direct-message ${message.senderId === me.id ? "outgoing" : "incoming"}`} key={message.id}>
        <small>{message.senderId === me.id ? "Вы" : conversation.peer.name} · <time dateTime={message.createdAt}>{relativeTime(message.createdAt)}</time></small>
        <p className="group-content">{message.isDeleted ? <em>Сообщение удалено</em> : message.isHidden ? <em>Сообщение скрыто модератором</em> : <MentionText text={message.text || ""} />}</p>
        {!message.isDeleted && !message.isHidden && <div className="content-tools">{message.senderId === me.id ? <button className="text-button" disabled={acting || sending} onClick={() => { if (window.confirm("Удалить сообщение для участников переписки?")) void action(() => api(`${base}/messages/${message.id}`, { method: "DELETE" }), () => { setMessages(previous => previous.map(m => m.id === message.id ? { ...m, text: null, isDeleted: true } : m)); }); }}>Удалить сообщение</button> : <button className="text-button" disabled={acting || sending} onClick={() => { setReportId(message.id); setReason(""); }}>Пожаловаться на сообщение</button>}</div>}
        {reportId === message.id && <form className="group-editor" onSubmit={e => { e.preventDefault(); void action(() => api(`${base}/messages/${message.id}/reports`, { method: "POST", body: JSON.stringify({ reason }) }), () => { setReportId(null); setInfo("Жалоба отправлена. Модератор увидит только выбранное сообщение и причину."); }); }}><label>Причина жалобы<textarea required minLength={5} maxLength={2000} value={reason} disabled={acting || sending} onChange={e => setReason(e.target.value)} /></label><p className="form-hint">Модератору будет передана копия только этого сообщения.</p><div className="content-tools"><button className="button secondary" disabled={acting || sending}>Отправить жалобу</button><button type="button" className="text-button" disabled={acting || sending} onClick={() => setReportId(null)}>Отмена</button></div></form>}
      </article>)}
    </div>
    {!atBottom && <button className="button secondary message-jump" onClick={() => setAtBottom(true)}>К последним сообщениям</button>}
    {sendError && <ErrorBox message={sendError} />}
    {conversation.canSend && <form className="profile-card group-editor message-compose" onSubmit={send}><label>Ваше сообщение<MentionTextarea required maxLength={4000} value={text} disabled={sending} onValueChange={setText} /></label><p className="form-hint">Упоминания здесь видят участники переписки; третьим лицам уведомления не отправляются.</p><button className="button primary" disabled={sending || !text.trim()}>{sending ? "Отправляем…" : "Отправить сообщение"}</button></form>}
  </>;
}
