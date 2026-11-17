import type { GroupEntry } from "../api";
import { relativeTime, userHref } from "../api";
import { Avatar } from "./ui";
import { RouteCover } from "./RouteCover";
import { postExcerpt } from "./postMarkup";
import { ArrowRight, MessageCircle } from "lucide-react";

export function GroupEntryCard({ entry }: { entry: GroupEntry }) {
  const href = `#/group-entry/${entry.id}`;
  const cover = entry.photos[0];
  return <article className="profile-card group-entry-card group-entry-preview">
    <div className="post-author">
      <a href={userHref(entry.author)}><Avatar user={entry.author} /></a>
      <div><a className="author-name" href={userHref(entry.author)}>{entry.author.name}</a>
        <div className="author-meta"><a href={`#/community/${entry.groupId}`}>{entry.groupName}</a><span>·</span>
          <time dateTime={entry.createdAt}>{relativeTime(entry.createdAt)}</time></div>
      </div><span className="post-kind">{entry.kind === 2 ? "Обсуждение" : "В сообществе"}</span>
    </div>
    <div className="group-entry-preview-body"><a href={href}><h2>{entry.title}</h2></a><p>{postExcerpt(entry.content)}</p></div>
    {!entry.photos.length && entry.hasRoute && <div className="group-entry-preview-media"><RouteCover previewUrl={`/group-entries/${entry.id}/route-preview`} href={href} /></div>}
    {cover && <a className="group-entry-preview-media" href={href}><img className="group-entry-cover" src={cover.imageUrl} alt="Фотография к записи" loading="lazy" /></a>}
    <footer className="group-entry-preview-footer"><a className="text-button" href={href}><MessageCircle size={17} aria-hidden="true" />{entry.repliesCount} ответов<ArrowRight size={15} aria-hidden="true" /></a></footer>
  </article>;
}
