import { ReplyQuote, ReplySelection, CommentTarget } from "../components/CommentReply";
import { userHref } from "../api";
import { RouteCover } from "../components/RouteCover";
import { PostRoute } from "../components/PostRoute";
import { PhotoGallery } from "../components/PhotoGallery";
import { PostText } from "../components/PostText";
import { MentionText } from "../components/MentionText";
import { MentionTextarea } from "../components/MentionTextarea";
import { galleryPhotos } from "../components/postMarkup";
import { ContentTools, ReportButton } from "../components/ContentTools";
import { useEffect, useRef, useState } from "react";
import type { FormEvent } from "react";
import { ArrowLeft, Send } from "lucide-react";
import { api, postCategories, relativeTime } from "../api";
import type { Comment, Page, Post, User } from "../api";
import { Avatar, ErrorBox, Loading } from "../components/ui";
import { PostActions } from "../components/PostCard";
import { appendUnique } from "../components/paging";

export function PostDetail({
  id,
  me,
  notify,
  targetComment,
}: {
  id: string;
  targetComment?: string | null;
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
  const [commentsError, setCommentsError] = useState("");
  const [commentsLoading, setCommentsLoading] = useState(true), [retry, setRetry] = useState(0), [postRetry, setPostRetry] = useState(0);
  const pending = useRef(false);
  const [replyTo, setReplyTo] = useState<Comment | null>(null);
  const form = useRef<HTMLFormElement>(null);
  const commentHref = (commentId: string) => `#/post/${id}?comment=${commentId}`;
  function selectReply(comment: Comment) { setReplyTo(comment); form.current?.querySelector("textarea")?.focus(); }
  const [text, setText] = useState("");
  const [sending, setSending] = useState(false);
  useEffect(() => {
    let active = true;
    setError("");
    api<Post>(`/posts/${id}`).then(p => { if (active) setPost(p); }).catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [id, revision, postRetry]);
  useEffect(() => {
    let active = true;
    pending.current = true; setCommentsLoading(true); setCommentsError("");
    api<Page<Comment>>(`/posts/${id}/comments?page=${page}&pageSize=20`)
      .then(c => {
        if (active) {
          setComments((previous) =>
            page === 1
              ? c.items
              : appendUnique(previous, c.items),
          );
          setMore(c.hasMore);
        }
      })
      .catch((e) => {
        if (active) setCommentsError(e.message);
      }).finally(() => { if (active) { pending.current = false; setCommentsLoading(false); } });
    return () => {
      active = false;
    };
  }, [id, page, revision, retry]);
  useEffect(() => {
    if (post && window.location.hash.includes("comments=1"))
      document
        .getElementById("comments")
        ?.scrollIntoView?.({ behavior: "smooth", block: "start" });
  }, [post?.id]);
  async function submit(e: FormEvent) {
    e.preventDefault();
    if (!text.trim() || sending || commentsLoading) return;
    setSending(true);
    try {
      const comment = await api<Comment>(`/posts/${id}/comments`, {
        method: "POST",
        body: JSON.stringify({ text: text.trim(), replyToId: replyTo?.id || null }),
      });
      setComments((c) => [comment, ...c]);
      setPost((p) => p && { ...p, commentsCount: p.commentsCount + 1 });
      setText(""); setReplyTo(null);
      notify("Комментарий добавлен");
    } catch (e) {
      notify((e as Error).message);
    } finally {
      setSending(false);
    }
  }
  if (error) return <ErrorBox message={error} retry={() => setPostRetry(v => v + 1)} />;
  if (!post) return <Loading />;
  const gallery = galleryPhotos(post.content, post.photos);
  return (
    <>
      <a className="back-link" href="#/">
        <ArrowLeft size={16} />В ленту
      </a>
      <ContentTools post={post} me={me} saved={() => { setPage(1); setRevision(v => v + 1); }} notify={notify} />
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
        {!post.photos?.length && post.route ? <RouteCover route={post.route} /> : (gallery.length > 0 || !post.photos?.length) && <PhotoGallery photos={gallery} fallback={post.imageUrl} title={post.title} className="post-photo-gallery" />}
        <div className="article-text">
          <PostText content={post.content} photos={post.photos} />
          <PostRoute copyHref={`#/route/new?post=${id}`} route={post.route} showMap={!!post.photos?.length} />
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
          Комментарии<span className="count">{post.commentsCount}</span>
        </h2>
        {me && <ReplySelection comment={replyTo} cancel={() => setReplyTo(null)} />}
        {me ? <form ref={form} className="comment-form" onSubmit={submit}>
          <Avatar user={me} small />
          <label>
            <span className="sr-only">Ваш комментарий</span>
            <MentionTextarea
              placeholder="Поделитесь опытом или задайте вопрос…"
              value={text}
              onValueChange={setText}
              maxLength={2000}
              required
            />
          </label>
          <button
            className="button primary"
            disabled={sending || commentsLoading || !text.trim()}
            aria-label="Отправить комментарий"
          >
            <Send size={18} />
          </button>
        </form> : <div className="auth-comment-prompt"><p>Войдите, чтобы присоединиться к обсуждению.</p>
          <button className="button" onClick={() => window.dispatchEvent(new Event("bikehub:auth-required"))}>Войти по почте</button></div>}
        <CommentTarget targetId={targetComment} endpoint={`/posts/${id}/comments`} visibleIds={comments.map(c => c.id)} href={commentHref} onReply={me ? selectReply : undefined} />
        <div className="comment-list">
          {comments.map((c) => (
            <div className={`comment${c.id === targetComment ? " comment-target" : ""}`} id={`comment-${c.id}`} tabIndex={-1} key={c.id}>
              <Avatar user={c.author} small />
              <div>
                <div className="comment-meta">
                  <a href={userHref(c.author)}>{c.author.name}</a>
                  <time>{relativeTime(c.createdAt)}</time>
                </div>
                <ReplyQuote comment={c} href={commentHref} />
                {editingComment === c.id && c.author.id === me?.id ? <form onSubmit={async e => {
                  e.preventDefault(); if (sending) return; setSending(true);
                  try { const next = await api<Comment>(`/posts/${id}/comments/${c.id}`, { method: "PUT", body: JSON.stringify({ text: commentText }) }); setComments(list => list.map(item => item.id === c.id ? next : item)); setEditingComment(null); }
                  catch (error) { notify((error as Error).message); } finally { setSending(false); }
                }}><MentionTextarea aria-label="Текст комментария" required maxLength={2000} value={commentText} onValueChange={setCommentText} />
                  <button className="text-button" disabled={sending}>Сохранить</button><button type="button" className="text-button" onClick={() => setEditingComment(null)}>Отмена</button></form> : <p><MentionText text={c.text} /></p>}
                {me && <button type="button" className="text-button" disabled={sending} onClick={() => selectReply(c)}>Ответить</button>}
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
        {commentsError && <ErrorBox message={commentsError} retry={() => setRetry(v => v + 1)} />}
        {commentsLoading && !comments.length && <Loading />}
        {!comments.length && !commentsLoading && !commentsError && (
          <p className="muted first-comment">
            Хорошая история заслуживает первого комментария.
          </p>
        )}
        {more && !commentsError && (
          <button className="load-more" disabled={commentsLoading} onClick={() => { if (!pending.current) { pending.current = true; setPage(p => p + 1); } }}>
            Ещё комментарии
          </button>
        )}
      </section>
    </>
  );
}
