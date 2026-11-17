import { useEffect, useState } from "react";
import { Bell, CheckCheck } from "lucide-react";
import { api, relativeTime, requestLogin } from "../api";
import type { Page, User } from "../api";
import { Empty, ErrorBox, Loading } from "../components/ui";

type Notification = {
  id: string; kind: number; actorId: string; actorName: string; actorNickname: string; avatarUrl: string | null;
  targetType: number; targetId: string; containerId: string | null; createdAt: string; isRead: boolean;
};
const changedEvent = "bikehub:notifications-changed";
const refresh = () => window.dispatchEvent(new Event(changedEvent));
const descriptions: Record<number, string> = { 1: "прокомментировал вашу запись", 2: "ответил вам", 3: "подписался на вас", 4: "упомянул вас", 5: "изменил покатушку — проверьте время и место", 6: "отменил покатушку", 7: "оставил отзыв о вашем маршруте", 8: "Пора проверить обслуживание велосипеда", 9: "отправил личное сообщение", 10: "отправил запрос на переписку" };
function destination(item: Notification) {
  if (item.targetType === 4) { return item.actorNickname ? `#/u/${encodeURIComponent(item.actorNickname)}` : `#/user/${item.actorId}`; }
  if (item.targetType === 2) { return `#/post/${item.targetId}`; }
  if (item.targetType === 3) { return `#/post/${item.containerId}?comment=${item.targetId}`; }
  if (item.targetType === 8) { return `#/group-entry/${item.containerId}?comment=${item.targetId}`; }
  if (item.targetType === 10) { return `#/ride/${item.containerId}`; }
  if (item.targetType === 12) { return `#/route/${item.containerId}`; }
  if (item.targetType === 13) { return `#/maintenance/${item.containerId}`; }
  if (item.targetType === 14) { return `#/message/${item.containerId}`; }
  return `#/group-entry/${item.targetId}`;
}

export function NotificationBell({ userId }: { userId: string }) {
  const [count, setCount] = useState(0);
  useEffect(() => {
    let active = true;
    setCount(0);
    const load = () => {
      if (document.hidden) { return; }
      api<number>("/notifications/unread-count").then(value => { if (active) { setCount(value); } }).catch(() => {});
    };
    load();
    const timer = setInterval(load, 60000);
    window.addEventListener(changedEvent, load); window.addEventListener("focus", load);
    return () => { active = false; clearInterval(timer); window.removeEventListener(changedEvent, load); window.removeEventListener("focus", load); };
  }, [userId]);
  return <a className="notification-bell" href="#/notifications" aria-label={count ? `Уведомления: ${count} непрочитанных` : "Уведомления"} title="Уведомления">
    <Bell size={20} />{count > 0 && <span>{count > 99 ? "99+" : count}</span>}
  </a>;
}

export function Notifications({ me }: { me: User | null }) {
  const [data, setData] = useState<Page<Notification> | null>(null), [page, setPage] = useState(1), [revision, setRevision] = useState(0);
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  useEffect(() => {
    let active = true; setData(null); setError("");
    if (me) { api<Page<Notification>>(`/notifications?page=${page}&pageSize=20`).then(result => { if (active) { setData(result); } }).catch(e => { if (active) { setError(e.message); } }); }
    return () => { active = false; };
  }, [me?.id, page, revision]);
  async function read(item?: Notification) {
    if (busy) { return; }
    setBusy(true); setError("");
    try {
      await api(`/notifications/${item ? `${item.id}/` : ""}read`, { method: "PUT" });
      refresh();
      if (item) { window.location.hash = destination(item).slice(1); }
      else { setRevision(value => value + 1); }
    } catch (e) { setError((e as Error).message); }
    finally { setBusy(false); }
  }
  if (!me) { return <Empty title="Ваши уведомления" description="Войдите, чтобы видеть ответы и упоминания." action={<button className="button primary" onClick={requestLogin}>Войти</button>} />; }
  return <><div className="page-heading"><div className="eyebrow">НА СВЯЗИ С СООБЩЕСТВОМ</div><h1>Уведомления</h1><p>Ответы, подписки и упоминания — всё здесь.</p></div>
    {error && <ErrorBox message={error} retry={() => setRevision(value => value + 1)} />}
    {!data ? !error && <Loading /> : <>
      {data.total > 0 && <button className="text-button notification-read-all" disabled={busy} onClick={() => void read()}><CheckCheck size={17} />Прочитать все</button>}
      <div className="notification-list">{data.items.map(item => <button type="button" key={item.id} className={`notification-item${item.isRead ? "" : " unread"}`} disabled={busy} onClick={() => void read(item)}>
        <span className="notification-avatar">{item.avatarUrl ? <img src={item.avatarUrl} alt="" /> : item.actorName.slice(0, 1)}</span>
        <span>{item.kind !== 8 && <strong>{item.actorName} </strong>}{descriptions[item.kind] || "отправил уведомление"}{item.actorNickname && item.kind !== 8 && <small>@{item.actorNickname}</small>}<time dateTime={item.createdAt}>{relativeTime(item.createdAt)}</time></span>
        {!item.isRead && <span className="notification-unread" aria-label="Не прочитано" />}
      </button>)}</div>
      {!data.items.length && <Empty title="Пока тихо" description="Здесь появятся комментарии к вашим записям, ответы, подписки и упоминания." />}
      {(page > 1 || data.hasMore) && <div className="content-tools"><button className="text-button" disabled={busy || page === 1} onClick={() => setPage(value => value - 1)}>Назад</button><span>Страница {page}</span><button className="text-button" disabled={busy || !data.hasMore} onClick={() => setPage(value => value + 1)}>Далее</button></div>}
    </>}
  </>;
}
