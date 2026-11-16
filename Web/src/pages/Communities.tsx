import { CommunityAvatar } from "../components/CommunityAvatar";
import { CommunityMembers } from "../components/CommunityMembers";
import { GroupEditor } from "./CommunityDetail";
import { requestLogin } from "../api";
import type { User } from "../api";
import { useEffect, useState } from "react";
import { Check, Plus, Search, Users } from "lucide-react";
import { api, categories, number, query } from "../api";
import type { Community, Page } from "../api";
import { Empty, ErrorBox, Loading } from "../components/ui";

export function Communities({
  version,
  me,
  notify,
  reload,
}: {
  version: number;
  me: User | null;
  notify: (s: string) => void;
  reload: () => void;
}) {
  const [membersOf, setMembersOf] = useState<Community | null>(null);
  const [creating, setCreating] = useState(false);
  const [search, setSearch] = useState("");
  const [input, setInput] = useState(""), [page, setPage] = useState(1), [data, setData] = useState<Page<Community> | null>(null);
  const [error, setError] = useState(""), [retry, setRetry] = useState(0);
  const [busy, setBusy] = useState<string | null>(null);
  useEffect(() => {
    let active = true; const controller = new AbortController(); setData(null); setError("");
    api<Page<Community>>(`/communities/catalog?${query({ q: search, page, pageSize: 20 })}`, { signal: controller.signal })
      .then(result => { if (active) { setData(result); } }).catch(e => { if (active) { setError(e.message); } });
    return () => { active = false; controller.abort(); };
  }, [search, page, version, retry, me?.id]);
  async function join(c: Community) {
    if (busy) return;
    if (!me) { requestLogin(); return; }
    setBusy(c.id);
    try {
      await api(`/communities/${c.id}/membership`, {
        method: c.isJoined ? "DELETE" : "PUT",
      });
      reload();
      notify(
        c.isJoined ? "Вы вышли из сообщества" : `Вы в сообществе «${c.name}»`,
      );
    } catch (e) {
      notify((e as Error).message);
    } finally {
      setBusy(null);
    }
  }
  return (
    <>
      <div className="page-heading">
        <div className="eyebrow">НА ОДНОЙ ВОЛНЕ</div>
        <h1>Свои найдутся.</h1>
        <p>Общий ритм, любимые маршруты и разговоры без финиша.</p>
      </div>
      <button className="button primary" onClick={() => { if (!me) { requestLogin(); return; } setCreating(!creating); }}>Создать сообщество</button>
      {creating && <GroupEditor close={() => setCreating(false)} saved={g => { reload(); window.location.hash = `/community/${g.id}`; }} />}
      <form className="directory-search search-form" role="search" onSubmit={event => { event.preventDefault(); setSearch(input.trim()); setPage(1); }}>
        <Search size={18} />
        <input
          aria-label="Поиск сообществ"
          placeholder="Найдите сообщество по душе"
          value={input}
          maxLength={200}
          onChange={(e) => setInput(e.target.value)}
        />
        <button type="submit" className="text-button">Найти</button>
        {search && <button type="button" className="text-button" onClick={() => { setInput(""); setSearch(""); setPage(1); }}>Сбросить</button>}
      </form>
      {membersOf && <CommunityMembers community={membersOf} close={() => setMembersOf(null)} />}
      {error ? <ErrorBox message={error} retry={() => setRetry(value => value + 1)} /> : !data ? <Loading /> : <>
      <div className="community-list">
        {data.items.map((c) => (
          <article className="community-row" key={c.id}>
            <a className="community-avatar-link" href={`#/community/${c.id}`} aria-label={`Открыть сообщество ${c.name}`}><CommunityAvatar community={c} /></a>
            <div className="community-row-body">
              <span className="eyebrow">{categories[c.category]}</span>
              <a href={`#/community/${c.id}`}><h2>{c.name}</h2></a>
              <p>{c.description}</p>
              <div className="community-bottom">
                <button type="button" className="community-members-button" onClick={() => setMembersOf(c)} aria-label={`Участники сообщества ${c.name}: ${number(c.members)}`}>
                  <Users size={15} />{number(c.members)} участников
                </button>
                <button
                  disabled={busy !== null}
                  className={`button ${c.isJoined ? "secondary" : "primary"}`}
                  onClick={() => join(c)}
                >
                  {c.isJoined ? <Check size={16} /> : <Plus size={16} />}
                  {c.isJoined ? "Вы участник" : "Вступить"}
                </button>
              </div>
            </div>
          </article>
        ))}
      </div>
      {!data.items.length && (
        <Empty
          title="Пока не нашли своих"
          description="Попробуйте поискать по другому слову."
        />
      )}
      <nav className="search-pagination" aria-label="Страницы сообществ"><button type="button" className="text-button" disabled={page === 1} onClick={() => setPage(value => value - 1)}>Назад</button><span>Страница {data.pageNumber} · Всего {data.total}</span><button type="button" className="text-button" disabled={!data.hasMore} onClick={() => setPage(value => value + 1)}>Далее</button></nav>
      </>}
    </>
  );
}
