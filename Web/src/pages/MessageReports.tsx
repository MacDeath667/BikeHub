import { useEffect, useState } from "react";
import { api } from "../api";
import type { MessagePeer, Page, User } from "../api";
import { Empty, ErrorBox, Loading } from "../components/ui";
import { MentionText } from "../components/MentionText";
import { PeerName } from "./Messages";

type MessageReport = { id: string; messageId: string | null; reporter: MessagePeer; reportedUser: MessagePeer; messageText: string; reason: string; createdAt: string; canChangeVisibility: boolean };
export function MessageReports({ me }: { me: User | null }) {
  const [data, setData] = useState<Page<MessageReport> | null>(null), [page, setPage] = useState(1), [revision, setRevision] = useState(0), [error, setError] = useState(""), [busy, setBusy] = useState(false);
  const [resolution, setResolution] = useState("");
  useEffect(() => { let active = true; setData(null); setError(""); if (me?.isModerator) api<Page<MessageReport>>(`/moderation/messages/reports?page=${page}&pageSize=20`).then(d => { if (active) setData(d); }).catch(e => { if (active) setError(e.message); }); return () => { active = false; }; }, [me?.id, me?.isModerator, page, revision]);
  if (!me?.isModerator) return <ErrorBox message="Необходимы права модератора." />;
  async function act(path: string, method: string, body: object) {
    if (busy) return; setBusy(true); setError("");
    try { await api(path, { method, body: JSON.stringify(body) }); setRevision(v => v + 1); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  return <><a className="back-link" href="#/moderation">← Модерация публичных материалов</a><div className="page-heading"><div className="eyebrow">ОБРАЩЕНИЯ УЧАСТНИКОВ</div><h1>Жалобы на личные сообщения</h1><p>Здесь только выбранные заявителями сообщения. Остальная переписка недоступна.</p></div>
    <label>Результат рассмотрения<textarea maxLength={2000} value={resolution} disabled={busy} onChange={e => setResolution(e.target.value)} /></label>
    {error && <ErrorBox message={error} retry={() => setRevision(v => v + 1)} />}{!data ? !error && <Loading /> : <>
      {data.items.map(report => <section className="profile-card" key={report.id}><p>Заявитель: <PeerName peer={report.reporter} /></p><p>Отправитель: <PeerName peer={report.reportedUser} /></p>
        <p className="muted">{new Date(report.createdAt).toLocaleString("ru-RU")}</p><h2>Сообщённый фрагмент</h2><blockquote className="reported-message"><MentionText text={report.messageText} /></blockquote><p><strong>Причина:</strong> {report.reason}</p>
        <div className="content-tools">{report.canChangeVisibility && report.messageId && <><button className="text-button" disabled={busy} onClick={() => void act(`/moderation/messages/${report.messageId}/visibility`, "PUT", { hidden: true })}>Скрыть сообщение</button><button className="text-button" disabled={busy} onClick={() => void act(`/moderation/messages/${report.messageId}/visibility`, "PUT", { hidden: false })}>Восстановить сообщение</button></>}
          {report.reportedUser.id && <><button className="text-button" disabled={busy} onClick={() => { if (window.confirm("Заблокировать аккаунт отправителя во всём BikeHub и завершить его сессии?")) void act(`/moderation/users/${report.reportedUser.id}/block`, "PUT", { blocked: true }); }}>Заблокировать аккаунт</button><button className="text-button" disabled={busy} onClick={() => void act(`/moderation/users/${report.reportedUser.id}/block`, "PUT", { blocked: false })}>Разблокировать аккаунт</button></>}
          <button className="button secondary" disabled={busy || resolution.trim().length < 2} onClick={() => void act(`/moderation/messages/reports/${report.id}/resolve`, "POST", { resolution })}>Закрыть жалобу</button>
        </div>
      </section>)}
      {!data.items.length && <Empty title="Открытых жалоб нет" description="Обращения по личной переписке появятся здесь." />}
      {(page > 1 || data.hasMore) && <div className="content-tools"><button className="text-button" disabled={busy || page === 1} onClick={() => setPage(v => v - 1)}>Назад</button><span>Страница {page}</span><button className="text-button" disabled={busy || !data.hasMore} onClick={() => setPage(v => v + 1)}>Далее</button></div>}</>}
  </>;
}
