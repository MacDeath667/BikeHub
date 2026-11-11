import { ArrowLeft, ChevronRight, UserRound } from "lucide-react";
import { requestLogin, userHref } from "../api";
import type { User } from "../api";
import { AccountDeletion } from "../components/AccountDeletion";
import { Empty } from "../components/ui";

export function Settings({ me, maskedEmail, account }: { me: User | null; maskedEmail: string | null; account: boolean }) {
  if (!me) return <Empty title="Настройки аккаунта" description="Войдите, чтобы управлять своим аккаунтом."
    action={<button className="button primary" onClick={requestLogin}>Войти</button>} />;
  if (!account) return <>
    <div className="page-heading"><div className="eyebrow">МОЁ ПРОСТРАНСТВО</div><h1>Настройки</h1><p>Управление вашим аккаунтом BikeHub.</p></div>
    <a className="settings-link profile-card" href="#/settings/account"><UserRound size={23} /><span><strong>Аккаунт</strong><small>Ник, почта и управление аккаунтом</small></span><ChevronRight size={19} /></a>
  </>;
  return <>
    <a className="back-link" href="#/settings"><ArrowLeft size={16} />Настройки</a>
    <div className="page-heading"><h1>Аккаунт</h1><p>Ваши данные и действия с аккаунтом.</p></div>
    <section className="profile-card settings-account"><h2>Данные аккаунта</h2><dl><dt>Ник</dt><dd>{me.handle ? `@${me.handle}` : "Ещё не выбран"}</dd><dt>Почта</dt><dd>{maskedEmail || "Подтверждена"}</dd></dl>
      <a className="text-button" href={userHref(me)}>Открыть профиль</a>
    </section>
    <section className="profile-card settings-account-management"><h2>Управление аккаунтом</h2><p className="form-hint">Удаление аккаунта и связанных с ним данных.</p><AccountDeletion /></section>
  </>;
}
