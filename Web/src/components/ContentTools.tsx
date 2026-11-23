import { useState } from "react";
import { api } from "../api";
import type { Bike, Post, User } from "../api";
import { CreateModal } from "./CreateModal";

export function ContentTools({ bike, post, me, saved, notify }: {
  bike?: Bike; post?: Post; me: User | null; saved: () => void; notify: (s: string) => void;
}) {
  const [editing, setEditing] = useState(false);
  const [busy, setBusy] = useState(false);
  const item = post || bike!;
  const own = me?.id === (post ? post.author.id : bike?.ownerId);
  const adminDeletion = !!post && !!me?.isAdmin && !own;
  async function remove() {
    if (busy) {
      return;
    }
    const confirmation = adminDeletion
      ? `Удалить запись «${post!.title}» пользователя ${post!.author.name}? Она исчезнет из ленты и бортжурнала, комментарии к ней станут недоступны.`
      : post ? "Удалить запись?" : "Удалить велосипед вместе с бортжурналом?";
    if (!window.confirm(confirmation)) {
      return;
    }
    setBusy(true);
    try {
      await api(`/${post ? "posts" : "bikes"}/${item.id}`, { method: "DELETE" });
      window.location.hash = post ? "/" : "/garage";
      saved();
      window.dispatchEvent(new Event("bikehub:content-changed"));
      notify(post ? "Запись удалена" : "Велосипед удалён");
    } catch (e) {
      notify((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return <div className="content-tools">
    {own && <button className="text-button" onClick={() => setEditing(true)}>Редактировать</button>}
    {(own || adminDeletion) && <button className={`text-button${adminDeletion ? " danger" : ""}`} disabled={busy} onClick={remove}>
      {adminDeletion ? "Удалить запись пользователя" : "Удалить"}
    </button>}
    {me && !own && <ReportButton type={post ? 2 : 1} id={item.id} notify={notify} />}
    {editing && own && me && <CreateModal key={`${me.id}-${item.id}`} type={post ? "post" : "bike"} me={me} initialBike={bike} initialPost={post}
      close={() => setEditing(false)} onSuccess={() => { setEditing(false); saved(); window.dispatchEvent(new Event("bikehub:content-changed")); notify("Изменения сохранены"); }} />}
  </div>;
}

export function ReportButton({ type, id, notify }: { type: number; id: string; notify: (s: string) => void }) {
  const [open, setOpen] = useState(false);
  const [busy, setBusy] = useState(false);
  const [reason, setReason] = useState("");
  return <><button className="text-button" onClick={() => setOpen(v => !v)}>Пожаловаться</button>
    {open && <form className="editor-form" onSubmit={async e => {
      e.preventDefault(); if (busy) return; setBusy(true);
      try { await api("/reports", { method: "POST", body: JSON.stringify({ targetType: type, targetId: id, reason }) }); setOpen(false); notify("Жалоба отправлена модератору"); }
      catch (e) { notify((e as Error).message); }
      finally { setBusy(false); }
    }}><label>Причина<textarea required minLength={5} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} /></label>
      <button className="button secondary" disabled={busy}>Отправить жалобу</button></form>}
  </>;
}
