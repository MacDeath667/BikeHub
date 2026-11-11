import { useId, useState } from "react";
import type { ReactNode } from "react";
import type { Photo } from "../api";
import { parsePostText } from "./postMarkup";
import type { PostTextNode } from "./postMarkup";

function Spoiler({ children }: { children: ReactNode }) {
  const [open, setOpen] = useState(false);
  const id = useId();
  return <span className="post-spoiler">
    <button type="button" aria-expanded={open} aria-controls={id} onClick={() => setOpen(value => !value)}>{open ? "Скрыть спойлер" : "Показать спойлер"}</button>
    <span id={id} hidden={!open}>{open && children}</span>
  </span>;
}

export function PostText({ content, photos = [] }: { content: string; photos?: Photo[] }) {
  const available = new Map(photos.map(photo => [photo.id.toLowerCase(), photo]));
  function render(nodes: PostTextNode[]): ReactNode {
    return nodes.map((node, index) => {
      switch (node.kind) {
        case "text": return node.text;
        case "image": {
          const photo = available.get(node.id);
          return photo ? <a className="post-inline-photo" key={index} href={photo.imageUrl} target="_blank" rel="noreferrer" aria-label={`Открыть фото: ${node.alt}`}>
            <img src={photo.imageUrl} alt={node.alt} loading="lazy" />
          </a> : <span key={index} className="muted">[Изображение недоступно]</span>;
        }
        case "bold": return <strong key={index}>{render(node.children)}</strong>;
        case "italic": return <em key={index}>{render(node.children)}</em>;
        case "underline": return <u key={index}>{render(node.children)}</u>;
        case "strike": return <s key={index}>{render(node.children)}</s>;
        case "spoiler": return <Spoiler key={index}>{render(node.children)}</Spoiler>;
      }
    });
  }
  return <div className="post-rich-text">{render(parsePostText(content))}</div>;
}
