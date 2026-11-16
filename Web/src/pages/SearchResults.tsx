import { useEffect, useState } from "react";
import { Search } from "lucide-react";
import { api, categories, query, relativeTime, userHref } from "../api";
import type { Community, GroupEntry, Page, Post, User } from "../api";
import { Avatar, Empty, ErrorBox, Loading } from "../components/ui";
import { CommunityAvatar } from "../components/CommunityAvatar";
import { PostCard } from "../components/PostCard";
import { postExcerpt } from "../components/postMarkup";

const tabs = { posts: "Записи", people: "Люди", communities: "Сообщества", discussions: "В сообществах" };
export type SearchKind = keyof typeof tabs;
export const searchKind = (value: string | null): SearchKind => value && Object.hasOwn(tabs, value) ? value as SearchKind : "posts";
export const searchHref = (q: string, kind: SearchKind, page = 1) => `#/search?${query({ q, kind, page })}`;
type Result = Post | User | Community | GroupEntry;

export function SearchResults({ search, kind, page, version, notify }: { search: string; kind: SearchKind; page: number; version: number; notify: (message: string) => void }) {
  const [input, setInput] = useState(search), [data, setData] = useState<Page<Result> | null>(null), [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  useEffect(() => setInput(search), [search]);
  useEffect(() => {
    let active = true; const controller = new AbortController(); setData(null); setError("");
    if (search.trim()) {
      api<Page<Result>>(`/search/${kind}?${query({ q: search, page, pageSize: 10 })}`, { signal: controller.signal })
        .then(result => { if (active) { setData(result); } }).catch(e => { if (active) { setError(e.message); } });
    }
    return () => { active = false; controller.abort(); };
  }, [search, kind, page, version, retry]);
  function update(post: Post) { setData(previous => previous && { ...previous, items: previous.items.map(item => item.id === post.id ? post : item) }); }
  return <>
    <div className="page-heading"><div className="eyebrow">ПОИСК ПО BIKEHUB</div><h1>Найдите своих.</h1><p>Истории, люди и разговоры о велосипедах.</p></div>
    <form className="directory-search search-form" role="search" onSubmit={e => { e.preventDefault(); window.location.hash = searchHref(input.trim(), kind).slice(1); }}>
      <Search size={18} aria-hidden="true" /><input aria-label="Поисковый запрос" value={input} maxLength={200} onChange={e => setInput(e.target.value)} placeholder="Например, гравий или @ник" />
      <button type="submit" className="text-button">Найти</button>
    </form>
    <nav className="search-tabs" aria-label="Тип результатов">{Object.entries(tabs).map(([key, label]) => <a className={`button ${kind === key ? "primary" : "secondary"}`} aria-current={kind === key ? "page" : undefined} href={searchHref(search, key as SearchKind)} key={key}>{label}</a>)}</nav>
    {!search.trim() ? <Empty title="С чего начнём?" description="Введите имя, ник, тему записи или название сообщества." /> : error ? <ErrorBox message={error} retry={() => setRetry(v => v + 1)} /> : !data ? <Loading /> : <>
      <p className="search-summary" role="status">По запросу «{search}»: {data.total} · {tabs[kind].toLocaleLowerCase("ru-RU")}</p>
      {!data.items.length ? <Empty title="Ничего не нашлось" description={page > 1 ? "На этой странице нет результатов. Вернитесь к началу поиска." : "Попробуйте другое слово или переключите тип результатов."} /> : <div className="search-results">
        {kind === "posts" && (data.items as Post[]).map(post => <PostCard key={post.id} post={post} update={update} notify={notify} />)}
        {kind === "people" && (data.items as User[]).map(user => <article className="profile-card search-person" key={user.id}><a href={userHref(user)} className="search-person-heading"><Avatar user={user} /><span><strong>{user.name}</strong><span className="muted">@{user.handle}</span></span></a>{user.city && <p>{user.city}</p>}<p>{user.bikesCount} велосипедов · {user.postsCount} записей</p></article>)}
        {kind === "communities" && (data.items as Community[]).map(group => <article className="profile-card search-community" key={group.id}><a className="search-person-heading" href={`#/community/${group.id}`}><CommunityAvatar community={group} /><span><strong>{group.name}</strong><span className="muted">{categories[group.category]} · {group.members} участников</span></span></a><p>{group.description}</p></article>)}
        {kind === "discussions" && (data.items as GroupEntry[]).map(entry => <article className="profile-card" key={entry.id}><a className="eyebrow" href={`#/community/${entry.groupId}`}>{entry.groupName} · {entry.kind === 1 ? "Пост" : "Обсуждение"}</a><a href={`#/group-entry/${entry.id}`}><h2>{entry.title}</h2></a><p>{postExcerpt(entry.content)}</p><div className="content-tools"><a href={userHref(entry.author)}>{entry.author.name}</a><span className="muted">{relativeTime(entry.createdAt)}</span><a href={`#/group-entry/${entry.id}`}>{entry.repliesCount} ответов →</a></div></article>)}
      </div>}
      <nav className="search-pagination" aria-label="Страницы результатов">
        {page > 1 && <a className="text-button" href={searchHref(search, kind, page - 1)}>← Назад</a>}
        {page > 1 && <a className="text-button" href={searchHref(search, kind)}>В начало</a>}
        <span>Страница {data.pageNumber}</span>
        {data.hasMore && <a className="text-button" href={searchHref(search, kind, page + 1)}>Далее →</a>}
      </nav>
    </>}
  </>;
}
