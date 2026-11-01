import { useEffect, useState } from "react";
import {
  ArrowDown,
  ArrowUpRight,
  Bike as BikeIcon,
  Plus,
  Settings2,
} from "lucide-react";
import { api, query } from "../api";
import type { Page, Post, User } from "../api";
import { Avatar, Empty, ErrorBox, Loading } from "../components/ui";
import { CategoryFilter } from "../components/CategoryFilter";
import { PostCard } from "../components/PostCard";

export function Feed({
  saved,
  search,
  version,
  me,
  notify,
  onCreate,
}: {
  saved: boolean;
  search: string;
  version: number;
  me: User | null;
  notify: (s: string) => void;
  onCreate: () => void;
}) {
  const [tab, setTab] = useState("all");
  const [category, setCategory] = useState("");
  const [page, setPage] = useState(1);
  const [data, setData] = useState<Page<Post> | null>(null);
  const [error, setError] = useState("");
  const [loading, setLoading] = useState(true);
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true;
    setLoading(true);
    setError("");
    api<Page<Post>>(
      `/feed?${query({ tab: saved ? "saved" : tab, category, q: search, page, pageSize: 5 })}`,
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
  }, [tab, saved, category, search, page, version, retry]);
  function update(post: Post) {
    setData(
      (previous) =>
        previous && {
          ...previous,
          total:
            previous.total -
            (saved &&
            !post.isSaved &&
            previous.items.some((p) => p.id === post.id)
              ? 1
              : 0),
          items: previous.items
            .map((p) => (p.id === post.id ? post : p))
            .filter((p) => !saved || p.isSaved),
        },
    );
  }
  return (
    <>
      {!saved && !search ? (
        <section className="feed-intro">
          <div className="eyebrow">
            <span className="online-dot" /> СООБЩЕСТВО УВЛЕЧЁННЫХ
          </div>
          <h1>
            Два колеса.
            <br />
            Тысячи историй<span>.</span>
          </h1>
          <p>
            Велосипеды, люди и всё, что между ними.
            <br />
            Делитесь опытом и вдохновляйтесь дорогой.
          </p>
          <div className="intro-bottom">
            <div className="avatar-stack">
              <span className="sage">АВ</span>
              <span className="sand">МС</span>
              <span className="rose">АБ</span>
              <span className="blue">ДО</span>
            </div>
            <span>
              Здесь есть свои.{" "}
              <a href="#/communities">
                Найдите и вы <ArrowUpRight size={13} />
              </a>
            </span>
          </div>
          <div className="intro-art" aria-hidden="true">
            <div className="art-ring ring-one" />
            <div className="art-ring ring-two" />
            <BikeIcon size={174} strokeWidth={0.8} />
            <span className="art-star">✳</span>
            <span className="art-caption">ЖИЗНЬ В ДВИЖЕНИИ / BIKEHUB</span>
          </div>
        </section>
      ) : (
        <div className="page-heading">
          <div className="eyebrow">ВАШ BIKEHUB</div>
          <h1>{saved ? "Хочется сохранить" : `Поиск: ${search}`}</h1>
          <p>
            {saved
              ? "Истории и полезный опыт, к которым стоит вернуться."
              : "Ищем в историях, велосипедах, именах и тегах."}
          </p>
        </div>
      )}
      {!saved && !search && (
        <button className="compose-prompt" onClick={onCreate}>
          {me ? <Avatar user={me} small /> : <BikeIcon size={24} />}
          <span>Какая история у вашего велосипеда?</span>
          <span className="compose-plus">
            <Plus size={18} />
          </span>
        </button>
      )}
      <div className="feed-toolbar">
        <div className="feed-tabs">
          {saved || search ? (
            <h2>
              {saved ? "Ваши закладки" : "Результаты"}
              {data && <span className="count">{data.total}</span>}
            </h2>
          ) : (
            [
              { id: "all", label: "Для вас" },
              { id: "following", label: "Подписки" },
              { id: "popular", label: "Популярное" },
            ].map((t) => (
              <button
                key={t.id}
                className={tab === t.id ? "active" : ""}
                aria-pressed={tab === t.id}
                onClick={() => {
                  setTab(t.id);
                  setPage(1);
                }}
              >
                {t.label}
                {t.id === "all" && <span className="tab-dot" />}
              </button>
            ))
          )}
        </div>
        <span className="sort-label">
          <Settings2 size={15} />
          {tab === "popular" ? "По лайкам" : "Свежее"}
        </span>
      </div>
      <CategoryFilter
        value={category}
        onChange={(v) => {
          setCategory(v);
          setPage(1);
        }}
      />
      {error ? (
        <ErrorBox message={error} retry={() => setRetry((v) => v + 1)} />
      ) : (
        <>
          {loading && page === 1 ? (
            <Loading />
          ) : data?.items.length ? (
            <div className="post-list">
              {data.items.map((post) => (
                <PostCard
                  key={post.id}
                  post={post}
                  update={update}
                  notify={notify}
                />
              ))}
            </div>
          ) : (
            <Empty
              title={
                saved
                  ? "Здесь будут ваши находки"
                  : tab === "following"
                    ? "Пока нет историй от подписок"
                    : "Пока ничего не нашлось"
              }
              description={
                saved
                  ? "Нажмите на закладку под записью, чтобы сохранить её здесь."
                  : "Попробуйте другой тип велосипеда, поиск или загляните в общую ленту."
              }
            />
          )}
          {data?.hasMore && (
            <button
              className="load-more"
              disabled={loading}
              onClick={() => setPage((p) => p + 1)}
            >
              {loading ? "Загружаем…" : "Больше историй"}
              <ArrowDown size={16} />
            </button>
          )}
        </>
      )}
    </>
  );
}
