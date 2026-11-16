import { ProfileEditor } from "../components/ProfileEditor";
import { ReportButton } from "../components/ContentTools";
import { useEffect, useRef, useState } from "react";
import { ArrowLeft, Bike as BikeIcon, Check, Plus } from "lucide-react";
import { api, number, requestLogin } from "../api";
import type { Bike, Page, User } from "../api";
import { Avatar, Empty, ErrorBox, Loading } from "../components/ui";
import { BikeCard } from "../components/BikeCard";
import { appendUnique } from "../components/paging";

export function UserProfile({
  id,
  byNickname = false,
  me,
  notify,
  reload,
  version,
}: {
  id: string;
  byNickname?: boolean;
  me: User | null;
  notify: (s: string) => void;
  reload: () => void;
  version: number;
}) {
  const [editing, setEditing] = useState(false);
  const [user, setUser] = useState<User | null>(null);
  const [bikes, setBikes] = useState<Bike[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  const [page, setPage] = useState(1), [retry, setRetry] = useState(0), [loading, setLoading] = useState(true), [more, setMore] = useState(false);
  const [total, setTotal] = useState<number | null>(null), pending = useRef(false);
  useEffect(() => {
    let active = true;
    setLoading(true); setError(""); pending.current = true;
    api<User>(byNickname ? `/users/by-nickname/${encodeURIComponent(id)}` : `/users/${id}`)
      .then(async u => {
        if (!active) return;
        setUser(u);
        const b = await api<Page<Bike>>(`/users/${u.id}/bikes?page=${page}&pageSize=20`);
        if (active) { setBikes(previous => page === 1 ? b.items : appendUnique(previous, b.items)); setMore(b.hasMore); setTotal(b.total); }
      })
      .catch((e) => {
        if (active) setError(e.message);
      }).finally(() => { if (active) { setLoading(false); pending.current = false; } });
    return () => {
      active = false;
    };
  }, [id, byNickname, version, page, retry]);
  async function follow() {
    if (!user || busy) return;
    if (!me) { requestLogin(); return; }
    setBusy(true);
    try {
      const next = await api<User>(`/users/${user.id}/follow`, {
        method: user.isFollowing ? "DELETE" : "PUT",
      });
      setUser(next);
      reload();
      notify(
        next.isFollowing
          ? "Теперь вы подписаны. Истории появятся в ленте подписок."
          : "Подписка отменена",
      );
    } catch (e) {
      notify((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  if (error && !user) return <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />;
  if (!user) return <Loading />;
  return (
    <>
      <a className="back-link" href="#/">
        <ArrowLeft size={16} />В сообщество
      </a>
      {editing && user.id === me?.id && <ProfileEditor key={me.id} user={user} close={() => setEditing(false)} saved={reload} />}
      {user.id === me?.id ? <button className="text-button" onClick={() => setEditing(true)}>Редактировать профиль</button> : me && <ReportButton type={4} id={user.id} notify={notify} />}
      {me?.id === user.id && me.isModerator && <a className="text-button" href="#/moderation">Модерация</a>}
      <section className="profile-card">
        <div className="profile-cover">
          <span>ЧЕЛОВЕК. ВЕЛОСИПЕД. ДОРОГА.</span>
          <BikeIcon size={170} strokeWidth={0.7} />
        </div>
        <div className="profile-body">
          <Avatar user={user} />
          <div className="profile-title">
            <div>
              <h1>{user.name}</h1>
              <span className="muted">
                {user.handle && <>@{user.handle} <span>·</span> </>}{user.city}
              </span>
            </div>
            {user.id !== me?.id && (
              <button
                className={`button ${user.isFollowing ? "secondary" : "primary"}`}
                disabled={busy}
                onClick={follow}
              >
                {user.isFollowing ? <Check size={17} /> : <Plus size={17} />}
                {user.isFollowing ? "Вы подписаны" : "Подписаться"}
              </button>
            )}
          </div>
          <p>{user.bio}</p>
          <div className="profile-stats">
            <span>
              <strong>{user.bikesCount}</strong> велосипеда
            </span>
            <span>
              <strong>{user.postsCount}</strong> записей
            </span>
            <span>
              <strong>{number(user.followers)}</strong> подписчиков
            </span>
          </div>
        </div>
      </section>
      <div className="section-heading">
        <h2>
          В гараже<span className="count">{total ?? user.bikesCount}</span>
        </h2>
      </div>
      <div className="bike-grid">
        {bikes.map((b) => (
          <BikeCard bike={b} key={b.id} />
        ))}
      </div>
      {error && <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />}
      {loading && !bikes.length && <Loading />}
      {!bikes.length && !loading && !error && (
        <Empty
          title="Гараж пока пуст"
          description="Первый велосипед ещё впереди."
        />
      )}
      {more && !error && <button className="load-more" disabled={loading} onClick={() => { if (!pending.current) { pending.current = true; setPage(p => p + 1); } }}>Ещё велосипеды</button>}
    </>
  );
}
