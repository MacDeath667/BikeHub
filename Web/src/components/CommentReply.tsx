import { useEffect, useState } from "react";
import type { Comment } from "../api";
import { api, userHref } from "../api";
import { ErrorBox, Loading } from "./ui";
import { MentionText } from "./MentionText";

export function ReplyQuote({ comment, href }: { comment: Comment; href: (id: string) => string }) {
  if (!comment.replyToId) return null;
  return <blockquote className="reply-quote">
    {comment.replyTo ? <a href={href(comment.replyToId)} aria-label={`К комментарию ${comment.replyTo.authorName}`}>
      <strong>{comment.replyTo.authorName}</strong><span>{comment.replyTo.text.slice(0, 240)}{comment.replyTo.text.length > 240 ? "…" : ""}</span>
    </a> : <span className="muted">Исходный комментарий недоступен</span>}
  </blockquote>;
}

export function ReplySelection({ comment, cancel }: { comment: Comment | null; cancel: () => void }) {
  return comment && <div className="reply-selection" role="status"><div>Ответ для <strong>{comment.author.name}</strong><p>{comment.text.slice(0, 160)}</p></div>
    <button type="button" className="text-button" onClick={cancel} aria-label="Отменить ответ на комментарий">Отмена</button></div>;
}

export function CommentTarget({ targetId, endpoint, visibleIds, href, onReply }: {
  targetId?: string | null; endpoint: string; visibleIds: string[];
  href: (id: string) => string; onReply?: (comment: Comment) => void;
}) {
  const [comment, setComment] = useState<Comment | null>(null), [error, setError] = useState(""), [retry, setRetry] = useState(0);
  useEffect(() => {
    let active = true; setComment(null); setError("");
    if (targetId) api<Comment>(`${endpoint}/${encodeURIComponent(targetId)}`).then(c => { if (active) setComment(c); }).catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [targetId, endpoint, retry]);
  const visible = !!targetId && visibleIds.includes(targetId);
  useEffect(() => {
    if (comment) {
      const element = document.getElementById(`comment-${comment.id}`);
      element?.scrollIntoView?.({ behavior: "smooth", block: "center" }); element?.focus({ preventScroll: true });
    }
  }, [comment, visible]);
  if (!targetId) return null;
  if (error) return <ErrorBox message={error} retry={() => setRetry(v => v + 1)} />;
  if (!comment) return <Loading />;
  if (visible) return null;
  return <section className="profile-card comment-target" id={`comment-${comment.id}`} tabIndex={-1} aria-label="Комментарий по ссылке">
    <small className="muted">Комментарий по ссылке</small><p><a href={userHref(comment.author)}><strong>{comment.author.name}</strong></a></p>
    <ReplyQuote comment={comment} href={href} /><p><MentionText text={comment.text} /></p>
    {onReply && <button type="button" className="text-button" onClick={() => onReply(comment)}>Ответить</button>}
  </section>;
}
