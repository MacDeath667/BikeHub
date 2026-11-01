import { useEffect, useState } from "react";
import type { FormEvent } from "react";
import { ArrowLeft, Send } from "lucide-react";
import { api, postCategories, relativeTime } from "../api";
import type { Comment, Page, Post, User } from "../api";
import { Avatar, ErrorBox, Loading } from "../components/ui";
import { PostActions } from "../components/PostCard";

export function PostDetail({
  id,
  me,
  notify,
}: {
  id: string;
  me: User | null;
  notify: (s: string) => void;
}) {
  const [post, setPost] = useState<Post | null>(null);
  const [comments, setComments] = useState<Comment[]>([]);
  const [page, setPage] = useState(1);
  const [more, setMore] = useState(false);
  const [error, setError] = useState("");
  const [text, setText] = useState("");
  const [sending, setSending] = useState(false);
  useEffect(() => {
    let active = true;
    Promise.all([
      api<Post>(`/posts/${id}`),
      api<Page<Comment>>(`/posts/${id}/comments?page=${page}&pageSize=20`),
    ])
      .then(([p, c]) => {
        if (active) {
          setPost(p);
          setComments((previous) =>
            page === 1
              ? c.items
              : [
                  ...new Map(
                    [...previous, ...c.items].map((item) => [item.id, item]),
                  ).values(),
                ],
          );
          setMore(c.hasMore);
        }
      })
      .catch((e) => {
        if (active) setError(e.message);
      });
    return () => {
      active = false;
    };
  }, [id, page]);
  useEffect(() => {
    if (post && window.location.hash.includes("comments=1"))
      document
        .getElementById("comments")
        ?.scrollIntoView({ behavior: "smooth", block: "start" });
  }, [post?.id]);
  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!text.trim() || sending) return;
    setSending(true);
    try {
      const comment = await api<Comment>(`/posts/${id}/comments`, {
        method: "POST",
        body: JSON.stringify({ text: text.trim() }),
      });
      setComments((c) => [comment, ...c]);
      setPost((p) => p && { ...p, commentsCount: p.commentsCount + 1 });
      setText("");
      notify("Комментарий добавлен");
    } catch (e) {
      notify((e as Error).message);
    } finally {
      setSending(false);
    }
  }
  if (error) return <ErrorBox message={error} />;
  if (!post) return <Loading />;
  return (
    <>
      <a className="back-link" href="#/">
        <ArrowLeft size={16} />В ленту
      </a>
      <article className="post-detail">
        <div className="detail-article-head">
          <span className={`post-kind ${post.category}`}>
            {postCategories[post.category]}
          </span>
          <h1>{post.title}</h1>
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
                {relativeTime(post.createdAt)}
              </div>
            </div>
          </div>
        </div>
        <img
          className="article-image"
          src={post.imageUrl}
          alt={post.bike.name}
        />
        <div className="article-text">
          {post.content.split("\n\n").map((paragraph, i) => (
            <p key={i}>{paragraph}</p>
          ))}
          <div className="post-tags">
            {post.tags.map((tag) => (
              <a key={tag} href={`#/?q=${encodeURIComponent(tag)}`}>
                #{tag}
              </a>
            ))}
          </div>
          <PostActions post={post} update={setPost} notify={notify} />
        </div>
      </article>
      <section className="comments-section" id="comments">
        <h2>
          Обсудим?<span className="count">{post.commentsCount}</span>
        </h2>
        {me ? <form className="comment-form" onSubmit={submit}>
          <Avatar user={me} small />
          <label>
            <span className="sr-only">Ваш комментарий</span>
            <textarea
              placeholder="Поделитесь опытом или задайте вопрос…"
              value={text}
              onChange={(e) => setText(e.target.value)}
              maxLength={2000}
              required
            />
          </label>
          <button
            className="button primary"
            disabled={sending || !text.trim()}
            aria-label="Отправить комментарий"
          >
            <Send size={18} />
          </button>
        </form> : <div className="auth-comment-prompt"><p>Войдите, чтобы присоединиться к обсуждению.</p>
          <button className="button" onClick={() => window.dispatchEvent(new Event("bikehub:auth-required"))}>Войти по почте</button></div>}
        <div className="comment-list">
          {comments.map((c) => (
            <div className="comment" key={c.id}>
              <Avatar user={c.author} small />
              <div>
                <div className="comment-meta">
                  <a href={`#/user/${c.author.id}`}>{c.author.name}</a>
                  <time>{relativeTime(c.createdAt)}</time>
                </div>
                <p>{c.text}</p>
              </div>
            </div>
          ))}
        </div>
        {!comments.length && (
          <p className="muted first-comment">
            Хорошая история заслуживает первого комментария.
          </p>
        )}
        {more && (
          <button className="load-more" onClick={() => setPage((p) => p + 1)}>
            Ещё комментарии
          </button>
        )}
      </section>
    </>
  );
}
