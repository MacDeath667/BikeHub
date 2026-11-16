import { mentionParts } from "./mentions";

export function MentionText({ text }: { text: string }) {
  return <>{mentionParts(text).map((part, index) => part.nickname
    ? <a className="mention-link" href={`#/u/${encodeURIComponent(part.nickname)}`} key={index}>{part.text}</a>
    : part.text)}</>;
}
