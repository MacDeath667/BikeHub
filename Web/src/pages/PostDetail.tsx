import { userHref } from "../api";
import { RouteCover } from "../components/RouteCover";
import { PostRoute } from "../components/PostRoute";
import { PhotoGallery } from "../components/PhotoGallery";
import { ContentTools, ReportButton } from "../components/ContentTools";
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
  const [revision, setRevision] = useState(0);
  const [editingComment, setEditingComment] = useState<string | null>(null);
  const [commentText, setCommentText] = useState("");
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
  }, [id, page, revision]);
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
      <ContentTools post={post} me={me} saved={() => setRevision(v => v + 1)} notify={notify} />
      <article className="post-detail">
        <div className="detail-article-head">
          <span className={`post-kind ${post.category}`}>
            {postCategories[post.category]}
          </span>
          <h1>{post.title}</h1>
          <div className="post-author">
            <a href={userHref(post.author)}>
              <Avatar user={post.author} />
            </a>
            <div>
              <a className="author-name" href={userHref(post.author)}>
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
        {!post.photos?.length && post.route ? <RouteCover route={post.route} /> : <PhotoGallery photos={post.photos} fallback={post.imageUrl} title={post.title} className="post-photo-gallery" />}
        <div className="article-text">
          {post.content.split("\n\n").map((paragraph, i) => (
            <p key={i}>{paragraph}</p>
          ))}
          <PostRoute route={post.route} showMap={!!post.photos?.length} />
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
                  <a href={userHref(c.author)}>{c.author.name}</a>
                  <time>{relativeTime(c.createdAt)}</time>
                </div>
                {editingComment === c.id ? <form onSubmit={async e => {
                  e.preventDefault(); if (sending) return; setSending(true);
                  try { const next = await api<Comment>(`/posts/${id}/comments/${c.id}`, { method: "PUT", body: JSON.stringify({ text: commentText }) }); setComments(list => list.map(item => item.id === c.id ? next : item)); setEditingComment(null); }
                  catch (error) { notify((error as Error).message); } finally { setSending(false); }
                }}><textarea aria-label="Текст комментария" required maxLength={2000} value={commentText} onChange={e => setCommentText(e.target.value)} />
                  <button className="text-button" disabled={sending}>Сохранить</button><button type="button" className="text-button" onClick={() => setEditingComment(null)}>Отмена</button></form> : <p>{c.text}</p>}
                {c.author.id === me?.id ? <><button className="text-button" onClick={() => { setEditingComment(c.id); setCommentText(c.text); }}>Изменить</button>
                  <button className="text-button" disabled={sending} onClick={async () => {
                    if (!window.confirm("Удалить комментарий?")) return; setSending(true);
                    try { await api(`/posts/${id}/comments/${c.id}`, { method: "DELETE" }); setComments(list => list.filter(item => item.id !== c.id)); setPost(p => p && ({ ...p, commentsCount: Math.max(0, p.commentsCount - 1) })); }
                    catch (error) { notify((error as Error).message); } finally { setSending(false); }
                  }}>Удалить</button></> : me && <ReportButton type={3} id={c.id} notify={notify} />}
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
