import { useEffect, useState } from "react";
import { api } from "../api";
import type { User } from "../api";
import { ErrorBox, Loading } from "../components/ui";

interface Overview { accounts: number; blockedAccounts: number; openReports: number; }

export function Admin({ me }: { me: User | null }) {
  const [data, setData] = useState<Overview | null>(null), [error, setError] = useState(""), [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true;
    setData(null); setError("");
    if (me?.isAdmin) {
      api<Overview>("/admin/overview").then(value => { if (active) { setData(value); } })
        .catch(error => { if (active) { setError(error.message); } });
    }
    return () => { active = false; };
  }, [me?.id, me?.isAdmin, retry]);
  if (!me?.isAdmin) {
    return <ErrorBox message="Необходимы права администратора." />;
  }
  return <>
    <div className="page-heading"><h1>Администрирование</h1></div>
    {error ? <ErrorBox message={error} retry={() => setRetry(value => value + 1)} /> : !data ? <Loading /> :
      <section className="profile-card settings-account"><h2>Участники и жалобы</h2><dl>
        <dt>Всего аккаунтов</dt><dd>{data.accounts}</dd>
        <dt>Заблокированные аккаунты</dt><dd>{data.blockedAccounts}</dd>
        <dt>Открытые жалобы на материалы</dt><dd>{data.openReports}</dd>
      </dl></section>}
    <section className="profile-card settings-account"><h2>Модерация</h2><div className="content-tools">
      <a className="button secondary" href="#/moderation">Публичные материалы</a>
      <a className="button secondary" href="#/message-reports">Жалобы на сообщения</a>
    </div></section>
  </>;
}
