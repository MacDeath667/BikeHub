import { useState } from "react";
import { api } from "../api";

export function PhotoUpload({ value, onChange, onBusy }: {
  value?: string | null; onChange: (id: string | null) => void; onBusy: (busy: boolean) => void;
}) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");
  const [preview, setPreview] = useState(value || "");
  return <div className="photo-upload">
    <label>Фотография
      <input type="file" accept="image/jpeg,image/png,image/webp" disabled={busy} onChange={async e => {
        const file = e.target.files?.[0];
        if (!file) return;
        if (file.size > 10 * 1024 * 1024) { setError("Максимальный размер — 10 МБ."); return; }
        setBusy(true); onBusy(true); setError("");
        try {
          const form = new FormData(); form.append("file", file);
          const image = await api<{ id: string; imageUrl: string }>("/media", { method: "POST", body: form });
          setPreview(image.imageUrl); onChange(image.id);
        } catch (error) { setError((error as Error).message); }
        finally { setBusy(false); onBusy(false); }
      }} />
    </label>
    {busy && <p role="status">Загружаем фотографию…</p>}
    {preview && <><img src={preview} alt="Выбранная фотография" style={{ maxWidth: "100%", maxHeight: 180, borderRadius: 12 }} />
      <button type="button" className="text-button" disabled={busy} onClick={() => { setPreview(""); onChange(null); }}>Убрать фотографию</button></>}
    {error && <p role="alert">{error}</p>}
    <small className="form-hint">JPEG, PNG или WebP, до 10 МБ. Геометки удаляются.</small>
  </div>;
}
