import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import type { User } from "../api";
import { ErrorBox } from "./ui";

export function ChooseNickname({ saved, logout }: { saved: (user: User) => void; logout: () => void }) {
  const dialog = useRef<HTMLDialogElement>(null);
  const [nickname, setNickname] = useState(""); const [busy, setBusy] = useState(false), [error, setError] = useState("");
  useEffect(() => { const element = dialog.current; element?.showModal(); return () => element?.close(); }, []);
  return <dialog ref={dialog} className="auth-dialog" aria-labelledby="choose-nickname-title" onCancel={e => e.preventDefault()}>
    <h2 id="choose-nickname-title">Выберите свой ник</h2><p className="auth-description">У вашего аккаунта пока нет ника. Он заменит технический идентификатор в профиле и станет адресом для будущих упоминаний.</p>
    <form className="auth-form" onSubmit={async event => {
      event.preventDefault(); if (busy) return; setBusy(true); setError("");
      try { saved(await api<User>("/users/me/nickname", { method: "POST", body: JSON.stringify({ nickname }) })); }
      catch (error) { setError((error as Error).message); } finally { setBusy(false); }
    }}><label htmlFor="choose-nickname">Ник</label><input id="choose-nickname" autoFocus autoComplete="username" autoCapitalize="none" spellCheck={false} required minLength={3} maxLength={32} pattern="[a-zA-Z0-9_]{3,32}" placeholder="gravel_rider" value={nickname} disabled={busy} onChange={e => setNickname(e.target.value)} />
      <p className="auth-footnote">3–32 символа: латинские буквы, цифры и подчёркивание. Регистр не учитывается. После выбора изменить ник нельзя.</p>
      {error && <ErrorBox message={error} />}<button className="button primary" disabled={busy}>Сохранить ник навсегда</button>
    </form><button className="text-button" disabled={busy} onClick={logout}>Выйти из аккаунта</button>
  </dialog>;
}
