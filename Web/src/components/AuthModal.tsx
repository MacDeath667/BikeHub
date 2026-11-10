import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { ArrowLeft, ArrowRight, Check, Mail, X } from "lucide-react";
import { api } from "../api";
import type { AuthSession } from "../api";

interface Challenge { challengeId: string; expiresAt: string; resendAt: string; }

export function AuthModal({ session, close, onSuccess }: {
  session: AuthSession | null;
  close: () => void;
  onSuccess: (session: AuthSession) => void;
}) {
  const dialog = useRef<HTMLDialogElement>(null);
  const codeInput = useRef<HTMLInputElement>(null);
  const [registering, setRegistering] = useState(false);
  const [email, setEmail] = useState("");
  const [name, setName] = useState("");
  const [code, setCode] = useState("");
  const [challenge, setChallenge] = useState<Challenge | null>(null);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [devCode, setDevCode] = useState("");
  const [now, setNow] = useState(Date.now());
  const remaining = challenge ? Math.max(0, Math.ceil((Date.parse(challenge.resendAt) - now) / 1000)) : 0;

  useEffect(() => {
    const element = dialog.current;
    if (element && !element.open) element.showModal();
    return () => { if (element?.open) element.close(); };
  }, []);
  useEffect(() => {
    const timer = setInterval(() => setNow(Date.now()), 1000);
    return () => clearInterval(timer);
  }, []);
  useEffect(() => { if (challenge) codeInput.current?.focus(); }, [challenge]);

  async function send(event?: FormEvent) {
    event?.preventDefault();
    if (busy) return;
    setBusy(true);
    setError("");
    try {
      const next = await api<Challenge>(registering ? "/auth/registration/email/challenges" : "/auth/login/email/challenges", {
        method: "POST", body: JSON.stringify(registering ? { email: email.trim(), name: name.trim() || null } : { email: email.trim() }),
      });
      setChallenge(next);
      setCode("");
      setDevCode("");
      setNow(Date.now());
    } catch (error) { setError((error as Error).message); }
    finally { setBusy(false); }
  }

  async function verify(event: FormEvent) {
    event.preventDefault();
    if (busy || !challenge) return;
    setBusy(true);
    setError("");
    try {
      onSuccess(await api<AuthSession>("/auth/email/verify", {
        method: "POST", body: JSON.stringify({ challengeId: challenge.challengeId, code }),
      }));
    } catch (error) { setError((error as Error).message); }
    finally { setBusy(false); }
  }

  async function viewMessage() {
    if (!challenge || busy) return;
    setBusy(true);
    setError("");
    try {
      const message = await api<{ code: string }>(`/auth/development/messages/${challenge.challengeId}`);
      setDevCode(message.code);
    } catch (error) { setError((error as Error).message); }
    finally { setBusy(false); }
  }

  return <dialog ref={dialog} className="auth-dialog" aria-labelledby="auth-title"
    onCancel={(event) => { event.preventDefault(); close(); }}
    onClick={(event) => { if (event.target === event.currentTarget) close(); }}>
    <button className="icon-button auth-close" aria-label="Закрыть окно входа" onClick={close}><X size={20} /></button>
    <div className="auth-symbol"><Mail size={28} strokeWidth={1.5} /></div>
    <div className="eyebrow">ВАШЕ МЕСТО В BIKEHUB</div>
    <h2 id="auth-title">{challenge ? "Проверьте почту" : registering ? "Регистрация в BikeHub" : "Вход в BikeHub"}</h2>
    {!session?.enabled ? <p className="auth-description">Вход временно недоступен. Попробуйте позже — истории и велосипеды доступны без регистрации.</p> : <>
      <p className="auth-description">{challenge
        ? <>Введите код из письма на <strong>{email}</strong>. Он подтвердит вашу почту и откроет аккаунт.</>
        : registering
          ? "Создайте аккаунт по почте. Отправим код для подтверждения — пароль не нужен."
          : "Укажите почту вашего аккаунта. Отправим одноразовый код для входа — пароль не нужен."}</p>
      {!challenge ? <form className="auth-form" onSubmit={send}>
        <label htmlFor="auth-email">Ваша почта</label>
        <input id="auth-email" type="email" autoComplete="email" placeholder="you@example.com" required maxLength={254}
          autoFocus value={email} disabled={busy} onChange={(event) => setEmail(event.target.value)} />
        {registering && <>
        <label htmlFor="auth-name">Как к вам обращаться <span>необязательно</span></label>
        <input id="auth-name" aria-describedby="auth-name-hint" autoComplete="nickname" placeholder="Имя для профиля" minLength={2} maxLength={60}
          value={name} disabled={busy} onChange={(event) => setName(event.target.value)} />
        <p id="auth-name-hint" className="auth-name-hint">Укажите имя для нового профиля. Уже зарегистрированы? Перейдите ко входу.</p>
        </>}
        <button className="button primary" disabled={busy}>{busy ? "Отправляем…" : "Получить код"}<ArrowRight size={17} /></button>
      </form> : <form className="auth-form" onSubmit={verify}>
        <label htmlFor="auth-code">Код из письма</label>
        <input ref={codeInput} id="auth-code" className="auth-code" inputMode="numeric" autoComplete="one-time-code"
          pattern="[0-9]{6}" maxLength={6} required placeholder="000000" value={code} disabled={busy}
          onChange={(event) => setCode(event.target.value.replace(/[^0-9]/g, ""))} />
        <button className="button primary" disabled={busy || code.length !== 6}>{busy ? "Проверяем…" : registering ? "Подтвердить и зарегистрироваться" : "Войти"}<Check size={17} /></button>
        <div className="auth-secondary">
          <button type="button" disabled={busy} onClick={() => { setChallenge(null); setError(""); setDevCode(""); }}><ArrowLeft size={14} /> Другая почта</button>
          <button type="button" disabled={busy || remaining > 0} onClick={() => send()}>{remaining ? `Ещё раз через ${remaining} с` : "Отправить ещё раз"}</button>
        </div>
      </form>}
      <div className="auth-secondary">
        <button type="button" disabled={busy} onClick={() => { setRegistering(!registering); setChallenge(null); setCode(""); setDevCode(""); setError(""); }}>
          {registering ? "Уже есть аккаунт? Войти" : "Нет аккаунта? Зарегистрироваться"}
        </button>
      </div>
      {error && <p className="auth-error" role="alert">{error}</p>}
      {session.developmentMailbox && <div className="auth-development">
        <strong>Тестовая почта</strong>
        <p>В режиме разработки письма остаются здесь и не отправляются адресату.</p>
        {challenge && (devCode
          ? <div className="auth-test-code">Код: <code>{devCode}</code><button type="button" onClick={() => { setCode(devCode); codeInput.current?.focus(); }}>Вставить</button></div>
          : <button type="button" disabled={busy} onClick={viewMessage}>Посмотреть тестовое письмо</button>)}
      </div>}
      <p className="auth-footnote">Почта используется для входа. В публичном профиле её не видно.</p>
    </>}
  </dialog>;
}
