import { GroupEditor } from "./CommunityDetail";
import { requestLogin } from "../api";
import type { User } from "../api";
import { useState } from "react";
import { Check, Plus, Search, Users } from "lucide-react";
import { api, categories, number } from "../api";
import type { Community } from "../api";
import { Empty } from "../components/ui";

export function Communities({
  communities,
  me,
  notify,
  reload,
}: {
  communities: Community[];
  me: User | null;
  notify: (s: string) => void;
  reload: () => void;
}) {
  const [creating, setCreating] = useState(false);
  const [search, setSearch] = useState("");
  const [busy, setBusy] = useState<string | null>(null);
  async function join(c: Community) {
    if (busy) return;
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
  const filtered = communities.filter((c) =>
    `${c.name} ${c.description}`.toLowerCase().includes(search.toLowerCase()),
  );
  return (
    <>
      <div className="page-heading">
        <div className="eyebrow">НА ОДНОЙ ВОЛНЕ</div>
        <h1>Свои найдутся.</h1>
        <p>Общий ритм, любимые маршруты и разговоры без финиша.</p>
      </div>
      <button className="button primary" onClick={() => { if (!me) { requestLogin(); return; } setCreating(!creating); }}>Создать сообщество</button>
      {creating && <GroupEditor close={() => setCreating(false)} saved={g => { reload(); window.location.hash = `/community/${g.id}`; }} />}
      <label className="directory-search">
        <Search size={18} />
        <input
          aria-label="Поиск сообществ"
          placeholder="Найдите сообщество по душе"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </label>
      <div className="community-grid">
        {filtered.map((c) => (
          <article className="community-card" key={c.id}>
            <div className="community-image">
              <img src={c.imageUrl} alt={c.name} />
              <span className="image-category">{categories[c.category]}</span>
            </div>
            <div className="community-body">
              <a href={`#/community/${c.id}`}><h2>{c.name}</h2></a>
              <p>{c.description}</p>
              <div className="community-bottom">
                <span>
                  <Users size={15} />
                  {number(c.members)}
                </span>
                <button
                  disabled={busy === c.id}
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
      {!filtered.length && (
        <Empty
          title="Пока не нашли своих"
          description="Попробуйте поискать по другому слову."
        />
      )}
    </>
  );
}
