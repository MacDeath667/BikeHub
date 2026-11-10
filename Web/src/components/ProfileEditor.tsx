import { useEffect, useRef, useState } from "react";
import { api } from "../api";
import type { User } from "../api";
import { PhotoUpload } from "./PhotoUpload";
import { ErrorBox } from "./ui";

export function ProfileEditor({ user, close, saved }: { user: User; close: () => void; saved: () => void }) {
  const ref = useRef<HTMLDialogElement>(null);
  const [avatarId, setAvatarId] = useState<string | null>(user.avatarUrl?.match(/\/media\/([\w-]+)/)?.[1] || null);
  const [uploading, setUploading] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState("");
  useEffect(() => { ref.current?.showModal(); return () => ref.current?.close(); }, []);
  return <dialog className="create-dialog" ref={ref} onCancel={e => { e.preventDefault(); if (!saving && !uploading) close(); }} aria-labelledby="profile-editor-title">
    <h2 id="profile-editor-title">Редактирование профиля</h2>
    <form className="editor-form" onSubmit={async e => {
      e.preventDefault(); if (saving || uploading) return;
      const values = Object.fromEntries(new FormData(e.currentTarget)); setSaving(true); setError("");
      try { await api("/users/me", { method: "PUT", body: JSON.stringify({ ...values, avatarId }) }); saved(); close(); }
      catch (e) { setError((e as Error).message); }
      finally { setSaving(false); }
    }}>
      <label>Имя<input name="name" defaultValue={user.name} minLength={2} maxLength={60} required /></label>
      <label>Адрес профиля<input name="handle" defaultValue={user.handle} pattern="[a-z0-9_]{3,32}" required /><small>От 3 до 32 символов: латинские буквы, цифры и подчёркивание.</small></label>
      <label>Город<input name="city" defaultValue={user.city} maxLength={100} /></label>
      <label>О себе<textarea name="bio" defaultValue={user.bio} maxLength={2000} /></label>
      <PhotoUpload value={user.avatarUrl} onChange={setAvatarId} onBusy={setUploading} />
      {error && <ErrorBox message={error} />}
      <div className="dialog-footer"><button type="button" className="button secondary" disabled={saving || uploading} onClick={close}>Отмена</button>
        <button className="button primary" disabled={saving || uploading}>Сохранить</button></div>
    </form>
  </dialog>;
}
