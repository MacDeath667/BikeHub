import { useEffect, useState } from "react";
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
    setError("");
    api<Page<Bike>>(
      `/bikes?${query({ category, q: debounced, ownerId: garage ? me?.id : undefined, page, pageSize: 8 })}`,
    )
      .then((next) => {
        if (active)
          setData((previous) =>
            page === 1
              ? next
              : { ...next, items: [...(previous?.items || []), ...next.items] },
          );
      })
      .catch((e) => {
        if (active) setError(e.message);
      })
      .finally(() => {
        if (active) setLoading(false);
      });
    return () => {
      active = false;
    };
  }, [garage, me?.id, category, debounced, version, page]);
  return (
    <>
      <div className="page-heading">
        <div className="eyebrow">
          {garage ? "ЛИЧНАЯ КОЛЛЕКЦИЯ" : "КАЖДЫЙ СО СВОИМ ХАРАКТЕРОМ"}
        </div>
        <h1>{garage ? "Мой гараж" : "Дело в велосипеде."}</h1>
        <p>
          {garage
            ? "Ваши велосипеды, километры и история каждого изменения."
            : "Знакомьтесь с велосипедами и теми, кто на них ездит."}
        </p>
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
      {error ? (
        <ErrorBox message={error} />
      ) : loading && page === 1 ? (
        <Loading />
      ) : data?.items.length ? (
        <div className="bike-grid">
          {data.items.map((b) => (
            <BikeCard key={b.id} bike={b} />
          ))}
        </div>
      ) : (
        <Empty
          title={
            garage
              ? "Освободите место для первой истории"
              : "Такой велосипед ещё не приехал"
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
      {data?.hasMore && (
        <button
          className="load-more"
          disabled={loading}
          onClick={() => setPage((p) => p + 1)}
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
  const [bike, setBike] = useState<Bike | null>(null);
  const [posts, setPosts] = useState<Post[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [more, setMore] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => {
    let active = true;
    Promise.all([
      api<Bike>(`/bikes/${id}`),
      api<Page<Post>>(`/bikes/${id}/history?page=${page}&pageSize=5`),
    ])
      .then(([b, p]) => {
        if (active) {
          setBike(b);
          setPosts((previous) =>
            page === 1 ? p.items : [...previous, ...p.items],
          );
          setMore(p.hasMore);
          setTotal(p.total);
        }
      })
      .catch((e) => {
        if (active) setError(e.message);
      });
    return () => {
      active = false;
    };
  }, [id, version, page]);
  if (error) return <ErrorBox message={error} />;
  if (!bike) return <Loading />;
  return (
    <>
      <a className="back-link" href="#/bikes">
        <ArrowLeft size={16} />К велосипедам
      </a>
      <section className="bike-detail">
        <div className="detail-cover">
          <img src={bike.imageUrl} alt={bike.name} />
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
            href={`#/user/${bike.ownerId}`}
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
      ) : (
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
      {more && (
        <button className="load-more" onClick={() => setPage((v) => v + 1)}>
          Ещё записи <ArrowDown size={16} />
        </button>
      )}
    </>
  );
}
