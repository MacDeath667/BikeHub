import { useRef, useState } from "react";
import { ImagePlus, Star, X } from "lucide-react";
import { api } from "../api";
import type { Photo } from "../api";

const maximumPhotos = 10;
const maximumBytes = 10 * 1024 * 1024;

export function PhotoCollection({ photos, onChange, onBusy, disabled = false }: {
  photos: Photo[]; onChange: (photos: Photo[]) => void; onBusy: (busy: boolean) => void; disabled?: boolean;
}) {
  const input = useRef<HTMLInputElement>(null);
  const uploading = useRef(false);
  const [progress, setProgress] = useState("");
  const [error, setError] = useState("");
  const [dragging, setDragging] = useState(false);
  async function upload(files: File[]) {
    if (uploading.current || disabled || !files.length) return;
    setError("");
    if (photos.length + files.length > maximumPhotos) { setError(`Можно добавить до ${maximumPhotos} фотографий.`); return; }
    uploading.current = true; onBusy(true);
    const next = [...photos];
    const failures: string[] = [];
    try {
      for (const [index, file] of files.entries()) {
        if (file.size > maximumBytes) { failures.push(`${file.name}: файл больше 10 МБ.`); continue; }
        if (!["image/jpeg", "image/png", "image/webp"].includes(file.type)) { failures.push(`${file.name}: выберите JPEG, PNG или WebP.`); continue; }
        setProgress(`Загружаем ${index + 1} из ${files.length}…`);
        try {
          const body = new FormData(); body.append("file", file);
          const photo = await api<Photo>("/media", { method: "POST", body });
          next.push({ id: photo.id, imageUrl: photo.imageUrl });
          onChange([...next]);
        } catch (error) { failures.push(`${file.name}: ${(error as Error).message}`); }
      }
    } finally {
      uploading.current = false; setProgress(""); onBusy(false);
      setError(failures.join(" "));
    }
  }
  const busy = !!progress || uploading.current || disabled;
  return <section className="photo-collection" aria-label="Фотографии">
    <div className="photo-collection-heading"><strong>Фотографии</strong><span>{photos.length} / {maximumPhotos}</span></div>
    <input ref={input} type="file" multiple accept="image/jpeg,image/png,image/webp" hidden onChange={event => {
      const files = Array.from(event.currentTarget.files || []); event.currentTarget.value = ""; void upload(files);
    }} />
    {photos.length > 0 && <div className="photo-picker-grid">
      {photos.map((photo, index) => <div className="photo-picker-item" key={photo.id}>
        <img src={photo.imageUrl} alt={`Фотография ${index + 1}`} />
        {index === 0 && <span className="photo-cover-badge"><Star size={12} />Обложка</span>}
        <button type="button" className="photo-remove" aria-label={`Убрать фотографию ${index + 1}`} disabled={busy}
          onClick={() => onChange(photos.filter(item => item.id !== photo.id))}><X size={16} /></button>
        {index > 0 && <button type="button" className="photo-make-cover" disabled={busy}
          onClick={() => onChange([photo, ...photos.filter(item => item.id !== photo.id)])}>Сделать обложкой</button>}
      </div>)}
    </div>}
    {photos.length < maximumPhotos && <button type="button" className={`photo-dropzone ${dragging ? "dragging" : ""}`} disabled={busy}
      onClick={() => input.current?.click()} onDragOver={event => { event.preventDefault(); if (!busy) setDragging(true); }}
      onDragLeave={() => setDragging(false)} onDrop={event => { event.preventDefault(); setDragging(false); if (!busy) void upload(Array.from(event.dataTransfer.files)); }}>
      <ImagePlus size={27} /><strong>{photos.length ? "Добавить фотографии" : "Добавьте фотографии"}</strong>
      <span>Выберите файлы или перетащите их сюда</span>
    </button>}
    {progress && <p role="status" className="form-hint">{progress}</p>}
    {error && <p role="alert" className="auth-error">{error}</p>}
    <small className="form-hint">До 10 фотографий, JPEG / PNG / WebP, до 10 МБ каждая. Первое фото станет обложкой.</small>
  </section>;
}
