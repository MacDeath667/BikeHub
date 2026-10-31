import { useEffect, useState } from "react";
import { ArrowLeft, Bike as BikeIcon, Check, Plus } from "lucide-react";
import { api, number } from "../api";
import type { Bike, Page, User } from "../api";
import { Avatar, Empty, ErrorBox, Loading } from "../components/ui";
import { BikeCard } from "../components/BikeCard";

export function UserProfile({
  id,
  me,
  notify,
  reload,
  version,
}: {
  id: string;
  me: User;
  notify: (s: string) => void;
  reload: () => void;
  version: number;
}) {
  const [user, setUser] = useState<User | null>(null);
  const [bikes, setBikes] = useState<Bike[]>([]);
  const [error, setError] = useState("");
  const [busy, setBusy] = useState(false);
  useEffect(() => {
    let active = true;
    Promise.all([
      api<User>(`/users/${id}`),
      api<Page<Bike>>(`/users/${id}/bikes?pageSize=50`),
    ])
      .then(([u, b]) => {
        if (active) {
          setUser(u);
          setBikes(b.items);
        }
      })
      .catch((e) => {
        if (active) setError(e.message);
      });
    return () => {
      active = false;
    };
  }, [id, version]);
  async function follow() {
    if (!user || busy) return;
    setBusy(true);
    try {
      const next = await api<User>(`/users/${id}/follow`, {
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
  if (error) return <ErrorBox message={error} />;
  if (!user) return <Loading />;
  return (
    <>
      <a className="back-link" href="#/">
        <ArrowLeft size={16} />В сообщество
      </a>
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
                @{user.handle} <span>·</span> {user.city}
              </span>
            </div>
            {id !== me.id && (
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
          В гараже<span className="count">{bikes.length}</span>
        </h2>
      </div>
      <div className="bike-grid">
        {bikes.map((b) => (
          <BikeCard bike={b} key={b.id} />
        ))}
      </div>
      {!bikes.length && (
        <Empty
          title="Гараж пока пуст"
          description="Первый велосипед ещё впереди."
        />
      )}
    </>
  );
}
