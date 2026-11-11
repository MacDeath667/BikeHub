import { useId, useRef, useState } from "react";
import { Bold, Eye, EyeOff, ImagePlus, Italic, Strikethrough, Underline } from "lucide-react";
import type { Photo } from "../api";
import { PhotoCollection } from "./PhotoCollection";
import { PostText } from "./PostText";
import { formatSelection, inlinePhotoIds, insertPhoto, maximumPostLength, removePhotoReferences } from "./postMarkup";
import type { TextFormat } from "./postMarkup";

const formats = [
  { marker: "**", label: "Жирный", Icon: Bold },
  { marker: "*", label: "Курсив", Icon: Italic },
  { marker: "__", label: "Подчёркнутый", Icon: Underline },
  { marker: "~~", label: "Зачёркнутый", Icon: Strikethrough },
  { marker: "||", label: "Спойлер", Icon: EyeOff },
] as const;

export function PostEditor({ value, onChange, photos, onPhotosChange, onBusy, disabled = false, label = "Ваша история", minLength }: {
  value: string; onChange: (value: string) => void; photos: Photo[]; onPhotosChange: (photos: Photo[]) => void;
  onBusy: (busy: boolean) => void; disabled?: boolean; label?: string; minLength?: number;
}) {
  const textarea = useRef<HTMLTextAreaElement>(null);
  const attachments = useRef<HTMLDivElement>(null);
  const selection = useRef({ start: value.length, end: value.length });
  const [preview, setPreview] = useState(false), [picking, setPicking] = useState(false), [error, setError] = useState("");
  const id = useId();
  const used = inlinePhotoIds(value, photos);
  function rememberSelection() {
    if (textarea.current) { selection.current = { start: textarea.current.selectionStart, end: textarea.current.selectionEnd }; }
  }
  function apply(next: { content: string; start: number; end: number }) {
    if (disabled) { return; }
    if (next.content.length > maximumPostLength) { setError("Текст с форматированием должен быть не длиннее 20 000 символов."); return; }
    setError(""); onChange(next.content); selection.current = next;
    requestAnimationFrame(() => { textarea.current?.focus(); textarea.current?.setSelectionRange(next.start, next.end); });
  }
  function format(marker: TextFormat) { apply(formatSelection(value, selection.current.start, selection.current.end, marker)); }
  function insert(photo: Photo) {
    apply(insertPhoto(value, selection.current.start, selection.current.end, photo.id)); setPicking(false);
  }
  function changePhotos(next: Photo[]) {
    const retained = new Set(next.map(photo => photo.id));
    const removed = photos.filter(photo => !retained.has(photo.id)).map(photo => photo.id);
    if (removed.length) { onChange(removePhotoReferences(value, removed)); }
    onPhotosChange(next);
  }
  return <div className="post-editor">
    <label htmlFor={id}>{label}</label>
    <div className="post-editor-toolbar" role="group" aria-label="Форматирование текста">
      {formats.map(({ marker, label, Icon }) => <button key={marker} type="button" title={label} aria-label={label} disabled={disabled}
        onMouseDown={event => event.preventDefault()} onClick={() => format(marker)}><Icon size={18} /></button>)}
      <button type="button" title="Вставить изображение" aria-label="Вставить изображение" aria-expanded={picking} aria-controls={`${id}-photos`} disabled={disabled}
        onMouseDown={event => event.preventDefault()} onClick={() => setPicking(value => !value)}><ImagePlus size={18} /></button>
      <button type="button" className="post-preview-toggle" aria-expanded={preview} aria-controls={`${id}-preview`} onClick={() => setPreview(value => !value)}><Eye size={17} /><span>Предпросмотр</span></button>
    </div>
    {picking && <div className="post-image-picker" id={`${id}-photos`}>
      <p>Выберите фото — оно появится в позиции курсора.</p>
      <div className="post-image-options">{photos.filter(photo => !used.has(photo.id.toLowerCase())).map((photo, index) => <button type="button" key={photo.id} disabled={disabled} onClick={() => insert(photo)} aria-label={`Вставить фотографию ${index + 1}`}><img src={photo.imageUrl} alt="" /></button>)}</div>
      <button type="button" className="text-button" onClick={() => { attachments.current?.scrollIntoView({ behavior: "smooth", block: "center" }); attachments.current?.querySelector<HTMLButtonElement>(".photo-dropzone")?.focus(); }}>Загрузить фотографии ↓</button>
    </div>}
    <textarea ref={textarea} id={id} name="content" required minLength={minLength} maxLength={maximumPostLength} rows={9} value={value} disabled={disabled}
      placeholder="Поделитесь деталями. Выделите текст и выберите форматирование."
      onSelect={rememberSelection} onBlur={rememberSelection} onChange={event => { onChange(event.target.value); rememberSelection(); setError(""); }}
      onKeyDown={event => {
        if ((event.ctrlKey || event.metaKey) && !event.altKey) {
          const marker = ({ i: "*", u: "__", b: "**" } as Record<string, TextFormat>)[event.key.toLowerCase()];
          if (marker) { event.preventDefault(); rememberSelection(); format(marker); }
        }
      }} />
    <div className="post-editor-hint"><span>Выделите текст для форматирования. Фото в тексте не повторяются в галерее.</span><span>{value.length} / {maximumPostLength}</span></div>
    {error && <p role="alert" className="auth-error">{error}</p>}
    {preview && <section id={`${id}-preview`} className="post-editor-preview" aria-label="Предпросмотр записи"><strong>Предпросмотр</strong>{value.trim() ? <PostText content={value} photos={photos} /> : <p className="muted">Здесь появится ваша история.</p>}</section>}
    <div ref={attachments}><PhotoCollection photos={photos} onChange={changePhotos} onBusy={onBusy} disabled={disabled} onInsert={insert} inlineIds={used} /></div>
  </div>;
}
