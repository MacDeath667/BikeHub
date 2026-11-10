import { useEffect, useRef, useState } from "react";
import { ChevronLeft, ChevronRight, Expand, X } from "lucide-react";
import type { Photo } from "../api";

export function PhotoGallery({ photos, fallback, title, className = "" }: {
  photos?: Photo[]; fallback: string; title: string; className?: string;
}) {
  const items = photos?.length ? photos : [{ id: "cover", imageUrl: fallback }];
  const [selected, setSelected] = useState(0);
  const [expanded, setExpanded] = useState(false);
  const dialog = useRef<HTMLDialogElement>(null);
  const current = Math.min(selected, items.length - 1);
  function move(offset: number) { setSelected(value => (value + offset + items.length) % items.length); }
  useEffect(() => {
    if (expanded) dialog.current?.showModal();
    else dialog.current?.close();
  }, [expanded]);
  return <div className={`photo-gallery ${className}`}>
    <button type="button" className="gallery-main" onClick={() => setExpanded(true)} aria-label={`Открыть фото: ${title}`}>
      <img src={items[current].imageUrl} alt={`${title} — фото ${current + 1}`} />
      <span className="gallery-expand"><Expand size={16} />{items.length > 1 && `${current + 1} / ${items.length}`}</span>
    </button>
    {items.length > 1 && <div className="gallery-thumbnails" aria-label="Выбор фотографии">
      {items.map((photo, index) => <button type="button" key={photo.id} className={index === current ? "active" : ""}
        aria-label={`Показать фото ${index + 1}`} aria-pressed={index === current} onClick={() => setSelected(index)}>
        <img src={photo.imageUrl} alt="" loading="lazy" />
      </button>)}
    </div>}
    <dialog ref={dialog} className="photo-lightbox" aria-label={`Фотографии: ${title}`} onCancel={event => { event.preventDefault(); setExpanded(false); }}
      onClick={event => { if (event.target === event.currentTarget) setExpanded(false); }}
      onKeyDown={event => { if (event.key === "ArrowLeft") { event.preventDefault(); move(-1); } if (event.key === "ArrowRight") { event.preventDefault(); move(1); } }}>
      <button type="button" className="lightbox-close" onClick={() => setExpanded(false)} aria-label="Закрыть фотографию"><X /></button>
      <img src={items[current].imageUrl} alt={`${title} — фото ${current + 1}`} />
      <div className="lightbox-controls">
        <button type="button" disabled={items.length < 2} onClick={() => move(-1)} aria-label="Предыдущее фото"><ChevronLeft /></button>
        <span>{current + 1} / {items.length}</span>
        <button type="button" disabled={items.length < 2} onClick={() => move(1)} aria-label="Следующее фото"><ChevronRight /></button>
      </div>
    </dialog>
  </div>;
}
