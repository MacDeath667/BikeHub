import { useEffect, useState } from "react";
import { api } from "../api";
import type { Page, User } from "../api";
import { Empty, ErrorBox, Loading } from "../components/ui";

type Report = { id: string; targetType: number; targetId: string; reason: string; createdAt: string; reporterId: string };
const names: Record<number, string> = { 1: "Велосипед", 2: "Запись", 3: "Комментарий", 4: "Пользователь", 5: "Обратная связь" };
export function Moderation({ me }: { me: User | null }) {
  const [reports, setReports] = useState<Report[] | null>(null);
  const [error, setError] = useState("");
  const [page, setPage] = useState(1);
  const [more, setMore] = useState(false);
  const [busy, setBusy] = useState(false);
  const [revision, setRevision] = useState(0);
  const [resolution, setResolution] = useState("");
  useEffect(() => {
    if (!me?.isModerator) return;
    let active = true;
    api<Page<Report>>(`/moderation/reports?page=${page}&pageSize=20`).then(data => {
      if (active) { setReports(data.items); setMore(data.hasMore); setError(""); }
    }).catch(error => { if (active) setError(error.message); });
    return () => { active = false; };
  }, [me?.id, me?.isModerator, page, revision]);
  if (!me?.isModerator) return <ErrorBox message="Необходимы права модератора." />;
  async function act(path: string, method: string, body: object) {
    if (busy) return; setBusy(true); setError("");
    try { await api(path, { method, body: JSON.stringify(body) }); setRevision(v => v + 1); }
    catch (e) { setError((e as Error).message); } finally { setBusy(false); }
  }
  return <><h1>Модерация</h1><p>Открытые жалобы пользователей.</p>
    {error && <ErrorBox message={error} />}
    <label>Результат проверки<textarea maxLength={2000} value={resolution} onChange={e => setResolution(e.target.value)} placeholder="Укажите результат перед закрытием жалобы" /></label>
    {!reports ? <Loading /> : !reports.length ? <Empty title="Жалоб нет" description="Открытые обращения появятся здесь." /> : reports.map(report => <section className="profile-card moderation-report" key={report.id}>
      <strong>{names[report.targetType]}</strong><p>{report.reason}</p>
      <small>{new Date(report.createdAt).toLocaleString("ru-RU")} · {report.targetId}</small>
      <div className="content-tools">
        {report.targetType <= 2 && <a className="text-button" href={`#/${report.targetType === 1 ? "bike" : "post"}/${report.targetId}`}>Открыть</a>}
        {report.targetType <= 3 && <><button className="text-button" disabled={busy} onClick={() => act(`/moderation/${report.targetType}/${report.targetId}/visibility`, "PUT", { hidden: true })}>Скрыть</button>
          <button className="text-button" disabled={busy} onClick={() => act(`/moderation/${report.targetType}/${report.targetId}/visibility`, "PUT", { hidden: false })}>Восстановить</button></>}
        {report.targetType === 4 && <><button className="text-button" disabled={busy} onClick={() => { if (window.confirm("Заблокировать аккаунт и завершить его сессии?")) act(`/moderation/users/${report.targetId}/block`, "PUT", { blocked: true }); }}>Заблокировать</button>
          <button className="text-button" disabled={busy} onClick={() => act(`/moderation/users/${report.targetId}/block`, "PUT", { blocked: false })}>Разблокировать</button></>}
        <button className="text-button" disabled={busy || resolution.trim().length < 2} onClick={() => act(`/moderation/reports/${report.id}/resolve`, "POST", { resolution })}>Закрыть жалобу</button>
      </div>
    </section>)}
    <div className="content-tools"><button className="text-button" disabled={page === 1} onClick={() => setPage(v => v - 1)}>Назад</button>
      <span>Страница {page}</span><button className="text-button" disabled={!more} onClick={() => setPage(v => v + 1)}>Далее</button></div>
  </>;
}
