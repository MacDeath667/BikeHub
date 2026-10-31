import { useState } from "react";
import {
  Bookmark,
  Heart,
  MapPin,
  MessageCircle,
  Sparkles,
  Wrench,
} from "lucide-react";
import { api, categories, number, postCategories, relativeTime } from "../api";
import type { Post } from "../api";
import { Avatar } from "./ui";

export function PostActions({
  post,
  update,
  notify,
}: {
  post: Post;
  update: (post: Post) => void;
  notify: (text: string) => void;
}) {
  const [busy, setBusy] = useState(false);
  async function mutate(kind: "like" | "bookmark") {
    if (busy) return;
    setBusy(true);
    try {
      const active = kind === "like" ? post.isLiked : post.isSaved;
      update(
        await api<Post>(`/posts/${post.id}/${kind}`, {
          method: active ? "DELETE" : "PUT",
        }),
      );
      if (kind === "bookmark")
        notify(
          active ? "Запись удалена из закладок" : "Запись сохранена в закладки",
        );
    } catch (e) {
      notify((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return (
    <div className="post-actions">
      <button
        disabled={busy}
        className={post.isLiked ? "liked" : ""}
        aria-label={post.isLiked ? "Убрать лайк" : "Нравится"}
        aria-pressed={post.isLiked}
        onClick={() => mutate("like")}
      >
        <Heart size={19} fill={post.isLiked ? "currentColor" : "none"} />
        {number(post.likes)}
      </button>
      <a
        href={`#/post/${post.id}?comments=1`}
        aria-label={`Комментарии: ${post.commentsCount}`}
      >
        <MessageCircle size={19} />
        {post.commentsCount}
        <span className="comments-label">Обсудить</span>
      </a>
      <span className="actions-spacer" />
      <button
        disabled={busy}
        aria-label={
          post.isSaved ? "Удалить из закладок" : "Сохранить в закладки"
        }
        aria-pressed={post.isSaved}
        className={post.isSaved ? "saved" : ""}
        onClick={() => mutate("bookmark")}
      >
        <Bookmark size={19} fill={post.isSaved ? "currentColor" : "none"} />
      </button>
    </div>
  );
}

export function PostCard({
  post,
  update,
  notify,
}: {
  post: Post;
  update: (post: Post) => void;
  notify: (s: string) => void;
}) {
  return (
    <article className="post-card">
      <div className="post-author">
        <a href={`#/user/${post.author.id}`}>
          <Avatar user={post.author} />
        </a>
        <div>
          <a className="author-name" href={`#/user/${post.author.id}`}>
            {post.author.name}
          </a>
          <div className="author-meta">
            <a href={`#/bike/${post.bike.id}`}>{post.bike.name}</a>
            <span>·</span>
            <time dateTime={post.createdAt}>
              {relativeTime(post.createdAt)}
            </time>
          </div>
        </div>
        <span className={`post-kind ${post.category}`}>
          {post.category === "maintenance" ? (
            <Wrench size={12} />
          ) : post.category === "upgrade" ? (
            <Sparkles size={12} />
          ) : null}
          {postCategories[post.category]}
        </span>
      </div>
      <a className="post-image" href={`#/post/${post.id}`}>
        <img src={post.imageUrl} alt={post.bike.name} loading="lazy" />
        <span className="image-category">{categories[post.bike.category]}</span>
        {post.category === "ride" && (
          <span className="image-caption">
            <MapPin size={13} /> Истории за пределами города
          </span>
        )}
      </a>
      <div className="post-content">
        <a href={`#/post/${post.id}`}>
          <h2>{post.title}</h2>
        </a>
        <p>{post.content.split("\n\n")[0]}</p>
        <div className="post-tags">
          {post.tags.map((tag) => (
            <a href={`#/?q=${encodeURIComponent(tag)}`} key={tag}>
              #{tag}
            </a>
          ))}
        </div>
        <PostActions post={post} update={update} notify={notify} />
      </div>
    </article>
  );
}
