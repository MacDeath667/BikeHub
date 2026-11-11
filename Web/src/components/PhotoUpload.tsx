import { useState } from "react";
import { api } from "../api";
import { AvatarCropper } from "./AvatarCropper";

export function PhotoUpload({ value, onChange, onBusy, label = "Фотография", disabled = false, avatar = false }: {
  label?: string; disabled?: boolean; avatar?: boolean;
  value?: string | null; onChange: (id: string | null) => void; onBusy: (busy: boolean) => void;
}) {
  const [busy, setBusy] = useState(false), [error, setError] = useState("");
  const [preview, setPreview] = useState(value || ""), [candidate, setCandidate] = useState<File | null>(null);
  async function upload(file: File) {
    setBusy(true);
    try {
      const form = new FormData(); form.append("file", file);
      const image = await api<{ id: string; imageUrl: string }>("/media", { method: "POST", body: form });
      setPreview(image.imageUrl); onChange(image.id);
    } finally { setBusy(false); }
  }
  return <div className="photo-upload">
    <label>{label}
      <input type="file" accept="image/jpeg,image/png,image/webp" disabled={busy || disabled || !!candidate} onChange={async event => {
        const file = event.currentTarget.files?.[0]; event.currentTarget.value = ""; if (!file) return;
        setError("");
        if (file.size > 10 * 1024 * 1024) { setError("Максимальный размер — 10 МБ."); return; }
        if (!["image/jpeg", "image/png", "image/webp"].includes(file.type)) { setError("Выберите фотографию JPEG, PNG или WebP."); return; }
        onBusy(true);
        if (avatar) { setCandidate(file); return; }
        try { await upload(file); } catch (error) { setError((error as Error).message); } finally { onBusy(false); }
      }} />
    </label>
    {candidate && <AvatarCropper file={candidate} uploading={busy} onCancel={() => { setCandidate(null); onBusy(false); }} onApply={async file => {
      await upload(file); setCandidate(null); onBusy(false);
    }} />}
    {busy && !candidate && <p role="status">Загружаем фотографию…</p>}
    {preview && !candidate && <><img src={preview} alt="Выбранная фотография" style={{ width: avatar ? 96 : undefined, height: avatar ? 96 : undefined, maxWidth: "100%", maxHeight: 180, borderRadius: avatar ? "50%" : 12 }} />
      <button type="button" className="text-button" disabled={busy || disabled} onClick={() => { setPreview(""); onChange(null); }}>Убрать фотографию</button></>}
    {error && <p role="alert">{error}</p>}
    <small className="form-hint">JPEG, PNG или WebP, до 10 МБ. Геометки удаляются.</small>
  </div>;
}
