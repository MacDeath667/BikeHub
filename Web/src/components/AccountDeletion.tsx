import { useEffect, useState } from "react";
import { api } from "../api";
import { ErrorBox } from "./ui";

type Challenge = { challengeId: string; expiresAt: string; resendAt: string };
export function AccountDeletion() {
  const [open, setOpen] = useState(false), [challenge, setChallenge] = useState<Challenge | null>(null);
  const [code, setCode] = useState(""), [busy, setBusy] = useState(false), [error, setError] = useState(""), [status, setStatus] = useState(""), [now, setNow] = useState(Date.now());
  useEffect(() => { if (!open) return; const timer = window.setInterval(() => setNow(Date.now()), 1000); return () => clearInterval(timer); }, [open]);
  useEffect(() => { if (!challenge) return; let active = true; const poll = () => api<{ status: string }>(`/users/me/deletion/${challenge.challengeId}/delivery`).then(s => { if (active) setStatus(s.status); }).catch(() => { if (active) setStatus("unavailable"); });
    void poll(); const timer = window.setInterval(poll, 4000); return () => { active = false; clearInterval(timer); }; }, [challenge]);
  async function act(path: string, body?: object) { if (busy) return; setBusy(true); setError(""); try {
    const result = await api<Challenge>(path, { method: "POST", ...(body ? { body: JSON.stringify(body) } : {}) });
    if (body) { window.location.hash = "/"; window.location.reload(); } else { setChallenge(result); setStatus("pending"); }
  } catch (e) { setError((e as Error).message); } finally { setBusy(false); } }
  const expired = challenge && now >= Date.parse(challenge.expiresAt);
  return <section className="account-deletion"><button className="text-button danger" onClick={() => setOpen(!open)}>Удалить аккаунт</button>{open && <div className="profile-card group-editor"><h2>Удаление аккаунта</h2>
    <p>Профиль, велосипеды, посты, ответы и фотографии будут удалены без возможности восстановления. Сообщества сохранятся для остальных участников.</p>
    <p>Для подтверждения отправим отдельный код на вашу подтверждённую почту.</p>
    {error && <ErrorBox message={error} />}
    {!challenge || expired ? <button className="button secondary" disabled={busy} onClick={() => act("/users/me/deletion")}>{expired ? "Запросить новый код удаления" : "Получить код удаления"}</button> : <>
      <p role="status">{status === "sent" ? "Код удаления отправлен на почту." : ["failed", "unknown", "unavailable"].includes(status) ? "Отправка не подтверждена. Проверьте почту; повтор доступен через минуту." : "Почтовый сервис обрабатывает запрос."}</p>
      <form onSubmit={e => { e.preventDefault(); if (window.confirm("Безвозвратно удалить аккаунт и все свои материалы?")) void act("/users/me/deletion/confirm", { challengeId: challenge.challengeId, code }); }}>
        <label>Код удаления<input required inputMode="numeric" autoComplete="one-time-code" pattern="[0-9]{6}" maxLength={6} value={code} onChange={e => setCode(e.target.value.replace(/\D/g, ""))} /></label>
        <button className="button secondary danger" disabled={busy || code.length !== 6}>Удалить навсегда</button>
      </form><button className="text-button" disabled={busy || now < Date.parse(challenge.resendAt)} onClick={() => act(`/users/me/deletion/${challenge.challengeId}/resend`)}>Повторить отправку{now < Date.parse(challenge.resendAt) ? ` через ${Math.ceil((Date.parse(challenge.resendAt) - now) / 1000)} с` : ""}</button>
    </>}
  </div>}</section>;
}
