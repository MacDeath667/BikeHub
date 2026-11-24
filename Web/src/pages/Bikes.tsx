import { userHref } from "../api";
import { PhotoGallery } from "../components/PhotoGallery";
import { ContentTools } from "../components/ContentTools";
import { useEffect, useRef, useState } from "react";
import {
  ArrowDown,
  ArrowLeft,
  ArrowUpRight,
  MapPin,
  Plus,
  Search,
} from "lucide-react";
import { api, categories, number, query } from "../api";
import type { Bike, Page, Post, User } from "../api";
import { Avatar, Empty, ErrorBox, Loading } from "../components/ui";
import { CategoryFilter } from "../components/CategoryFilter";
import { PostCard } from "../components/PostCard";
import { BikeCard } from "../components/BikeCard";
import { appendUnique } from "../components/paging";

export function BikeDirectory({
  garage,
  me,
  version,
  onCreate,
}: {
  garage: boolean;
  me: User | null;
  version: number;
  onCreate: () => void;
}) {
  const [category, setCategory] = useState("");
  const [search, setSearch] = useState("");
  const [debounced, setDebounced] = useState("");
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Page<Bike> | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  const pending = useRef(false);
  useEffect(() => {
    const timer = setTimeout(() => {
      setDebounced(search);
      setPage(1);
    }, 250);
    return () => clearTimeout(timer);
  }, [search]);
  useEffect(() => {
    let active = true;
    setLoading(true);
    pending.current = true;
    setError("");
    if (page === 1) setData(null);
    api<Page<Bike>>(
      `/bikes?${query({ category, q: debounced, ownerId: garage ? me?.id : undefined, page, pageSize: 8 })}`,
    )
      .then((next) => {
        if (active)
          setData((previous) =>
            page === 1
              ? next
              : { ...next, items: appendUnique(previous?.items || [], next.items) },
          );
      })
      .catch((e) => {
        if (active) setError(e.message);
      })
      .finally(() => {
        if (active) { pending.current = false; setLoading(false); }
      });
    return () => {
      active = false;
    };
  }, [garage, me?.id, category, debounced, version, page, retry]);
  return (
    <>
      <div className="page-heading">
        <h1>{garage ? "Мой гараж" : "Велосипеды"}</h1>
        <button className="button primary heading-action" onClick={onCreate}>
          <Plus size={17} />
          Добавить велосипед
        </button>
      </div>
      <label className="directory-search">
        <Search size={18} />
        <input
          placeholder="Марка, модель или владелец"
          aria-label="Поиск велосипедов"
          maxLength={200}
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
      </label>
      <CategoryFilter
        value={category}
        onChange={(v) => {
          setCategory(v);
          setPage(1);
        }}
      />
      {error && !data ? (
        <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />
      ) : loading && page === 1 ? (
        <Loading />
      ) : data?.items.length ? (
        <div className="bike-grid">
          {data.items.map((b) => (
            <BikeCard key={b.id} bike={b} />
          ))}
        </div>
      ) : !error && (
        <Empty
          title={
            garage
              ? "Гараж пуст"
              : "Велосипеды не найдены"
          }
          description={
            garage
              ? "Добавьте велосипед и начните вести его бортжурнал."
              : "Попробуйте другую марку или категорию."
          }
          action={
            garage && (
              <button className="button primary" onClick={onCreate}>
                Добавить велосипед
              </button>
            )
          }
        />
      )}
      {error && data && <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />}
      {data?.hasMore && !error && (
        <button
          className="load-more"
          disabled={loading}
          onClick={() => { if (!pending.current) { pending.current = true; setPage(p => p + 1); } }}
        >
          Показать ещё <ArrowDown size={16} />
        </button>
      )}
    </>
  );
}

export function BikeDetail({
  id,
  me,
  version,
  onCreate,
  notify,
}: {
  id: string;
  me: User | null;
  version: number;
  onCreate: () => void;
  notify: (s: string) => void;
}) {
  const [revision, setRevision] = useState(0);
  const [bike, setBike] = useState<Bike | null>(null);
  const [posts, setPosts] = useState<Post[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [more, setMore] = useState(false);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true), [retry, setRetry] = useState(0);
  const pending = useRef(false);
  useEffect(() => {
    let active = true;
    setLoading(true); setError(""); pending.current = true;
    Promise.all([
      api<Bike>(`/bikes/${id}`),
      api<Page<Post>>(`/bikes/${id}/history?page=${page}&pageSize=5`),
    ])
      .then(([b, p]) => {
        if (active) {
          setBike(b);
          setPosts((previous) =>
            page === 1 ? p.items : appendUnique(previous, p.items),
          );
          setMore(p.hasMore);
          setTotal(p.total);
        }
      })
      .catch((e) => {
        if (active) setError(e.message);
      }).finally(() => { if (active) { pending.current = false; setLoading(false); } });
    return () => {
      active = false;
    };
  }, [id, version, page, revision, retry]);
  if (error && !bike) return <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />;
  if (!bike) return <Loading />;
  return (
    <>
      <a className="back-link" href="#/bikes">
        <ArrowLeft size={16} />К велосипедам
      </a>
      <ContentTools bike={bike} me={me} saved={() => { setPage(1); setRevision(v => v + 1); }} notify={notify} />
      <section className="bike-detail">
        <div className="detail-cover">
          <PhotoGallery photos={bike.photos} fallback={bike.imageUrl} title={bike.name} />
          <span className="image-category">{categories[bike.category]}</span>
        </div>
        <div className="detail-body">
          <div className="eyebrow">
            {bike.brand} / {bike.year}
          </div>
          <h1>{bike.name}</h1>
          <p>{bike.description}</p>
          <div className="bike-specs">
            <span>
              <strong>{number(bike.mileage)} км</strong>Пробег
            </span>
            <span>
              <strong>{bike.year}</strong>Год выпуска
            </span>
            <span>
              <strong>{categories[bike.category]}</strong>Тип велосипеда
            </span>
          </div>
          <a
            className="bike-owner detail-owner"
            href={userHref(bike.owner)}
          >
            <Avatar user={bike.owner} />
            <span>
              <strong>{bike.owner.name}</strong>
              <small>
                <MapPin size={12} />
                {bike.owner.city}
              </small>
            </span>
            <ArrowUpRight size={18} />
          </a>
        </div>
      </section>
      <div className="section-heading">
        {bike.ownerId === me?.id && <a className="text-button" href={`#/maintenance/${bike.id}`}>Компоненты и обслуживание</a>}
        <h2>
          Бортжурнал<span className="count">{total}</span>
        </h2>
        {bike.ownerId === me?.id && (
          <button className="text-button" onClick={onCreate}>
            <Plus size={16} />
            Новая запись
          </button>
        )}
      </div>
      {posts.length ? (
        <div className="post-list">
          {posts.map((p) => (
            <PostCard
              me={me}
              key={p.id}
              post={p}
              update={(next) =>
                setPosts((list) =>
                  list.map((item) => (item.id === next.id ? next : item)),
                )
              }
              notify={notify}
            />
          ))}
        </div>
      ) : !loading && !error && (
        <Empty
          title="История только начинается"
          description="Здесь появятся поездки, обслуживание и обновления этого велосипеда."
          action={
            bike.ownerId === me?.id && (
              <button className="button primary" onClick={onCreate}>
                Написать первую запись
              </button>
            )
          }
        />
      )}
      {error && <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />}
      {loading && !posts.length && <Loading />}
      {more && !error && (
        <button className="load-more" disabled={loading} onClick={() => { if (!pending.current) { pending.current = true; setPage(v => v + 1); } }}>
          Ещё записи <ArrowDown size={16} />
        </button>
      )}
    </>
  );
}
