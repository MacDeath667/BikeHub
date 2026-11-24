import { useEffect, useId, useRef, useState } from "react";
import type { ReactNode } from "react";
import { Ellipsis, Flag, Pencil, ShieldCheck, Trash2 } from "lucide-react";
import { api } from "../api";
import type { Bike, Post, User } from "../api";
import { CreateModal } from "./CreateModal";

export function ContentTools({ bike, post, me, saved, deleted, redirectAfterDelete = true, loadPostForEditing = false, compact = false, notify }: {
  bike?: Bike; post?: Post; me: User | null; saved: () => void; notify: (s: string) => void;
  deleted?: () => void; redirectAfterDelete?: boolean;
  loadPostForEditing?: boolean;
  compact?: boolean;
}) {
  const [editing, setEditing] = useState(false);
  const [editorPost, setEditorPost] = useState<Post | undefined>(undefined);
  const [busy, setBusy] = useState(false);
  const [reporting, setReporting] = useState(false);
  const item = post || bike!;
  const own = me?.id === (post ? post.author.id : bike?.ownerId);
  const adminDeletion = !!post && !!me?.isAdmin && !own;
  const staff = !own && !!(me?.isModerator || me?.isAdmin);
  async function edit() {
    if (busy) {
      return;
    }
    if (!post || !loadPostForEditing) {
      setEditing(true);
      return;
    }
    setBusy(true);
    try {
      setEditorPost(await api<Post>(`/posts/${post.id}`));
      setEditing(true);
    } catch (e) {
      notify((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
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
      if (redirectAfterDelete) {
        window.location.hash = post ? "/" : "/garage";
      }
      (deleted || saved)();
      window.dispatchEvent(new Event("bikehub:content-changed"));
      notify(post ? "Запись удалена" : "Велосипед удалён");
    } catch (e) {
      notify((e as Error).message);
    } finally {
      setBusy(false);
    }
  }
  return <div className="content-tools">
    {compact ? me && <ContentMenu key={`${me.id}-${item.id}-${own}-${staff}`} staff={staff} busy={busy}
      label={staff ? "Меню модерации" : post ? "Действия с записью" : "Действия с велосипедом"}>
      {close => <>
        {staff && <div className="content-menu-heading" role="presentation">Модерация</div>}
        {own && <button type="button" role="menuitem" tabIndex={-1} onClick={() => { close(); void edit(); }}><Pencil size={17} aria-hidden="true" />Редактировать</button>}
        {(own || adminDeletion) && <button type="button" role="menuitem" tabIndex={-1} className="danger" onClick={() => { close(); void remove(); }}>
          <Trash2 size={17} aria-hidden="true" />{adminDeletion ? "Удалить запись пользователя" : "Удалить"}
        </button>}
        {!own && <button type="button" role="menuitem" tabIndex={-1} onClick={() => { close(); setReporting(true); }}><Flag size={17} aria-hidden="true" />Пожаловаться</button>}
        {staff && <a role="menuitem" tabIndex={-1} href="#/moderation" onClick={close}><ShieldCheck size={17} aria-hidden="true" />Жалобы на материалы</a>}
      </>}
    </ContentMenu> : <>
      {own && <button className="text-button" disabled={busy} onClick={edit}>Редактировать</button>}
      {(own || adminDeletion) && <button className={`text-button${adminDeletion ? " danger" : ""}`} disabled={busy} onClick={remove}>
        {adminDeletion ? "Удалить запись пользователя" : "Удалить"}
      </button>}
      {me && !own && <ReportButton type={post ? 2 : 1} id={item.id} notify={notify} />}
    </>}
    {compact && reporting && me && !own && <ReportForm key={`${me.id}-${item.id}`} type={post ? 2 : 1} id={item.id} notify={notify} close={() => setReporting(false)} />}
    {editing && own && me && <CreateModal key={`${me.id}-${item.id}`} type={post ? "post" : "bike"} me={me} initialBike={bike} initialPost={editorPost || post}
      close={() => setEditing(false)} onSuccess={() => { setEditing(false); saved(); window.dispatchEvent(new Event("bikehub:content-changed")); notify("Изменения сохранены"); }} />}
  </div>;
}

function ContentMenu({ staff, busy, label, children }: {
  staff: boolean; busy: boolean; label: string; children: (close: () => void) => ReactNode;
}) {
  const [open, setOpen] = useState(false);
  const root = useRef<HTMLDivElement>(null), trigger = useRef<HTMLButtonElement>(null), menu = useRef<HTMLDivElement>(null);
  const lastFirst = useRef(false);
  const id = useId();
  function close() {
    setOpen(false);
    trigger.current?.focus();
  }
  useEffect(() => {
    if (!open) {
      return;
    }
    const items = menu.current?.querySelectorAll<HTMLElement>('[role="menuitem"]');
    items?.[lastFirst.current ? items.length - 1 : 0]?.focus();
    const outside = (event: PointerEvent) => {
      if (!root.current?.contains(event.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener("pointerdown", outside, true);
    return () => document.removeEventListener("pointerdown", outside, true);
  }, [open]);
  const Icon = staff ? ShieldCheck : Ellipsis;
  return <div ref={root} className="content-menu" onBlur={event => {
    if (!event.currentTarget.contains(event.relatedTarget)) {
      setOpen(false);
    }
  }} onKeyDown={event => {
    if (open && event.key === "Escape") {
      event.preventDefault(); event.stopPropagation(); close();
      return;
    }
    if (!["ArrowDown", "ArrowUp", "Home", "End"].includes(event.key) || busy) {
      return;
    }
    if (!open) {
      if (event.key === "ArrowDown" || event.key === "ArrowUp") {
        event.preventDefault(); lastFirst.current = event.key === "ArrowUp"; setOpen(true);
      }
      return;
    }
    event.preventDefault();
    const items = Array.from(menu.current?.querySelectorAll<HTMLElement>('[role="menuitem"]') || []);
    const index = items.indexOf(document.activeElement as HTMLElement);
    const next = event.key === "Home" ? 0 : event.key === "End" ? items.length - 1 : (index + (event.key === "ArrowUp" ? items.length - 1 : 1)) % items.length;
    items[next]?.focus();
  }}>
    <button ref={trigger} id={`${id}-trigger`} type="button" className="content-menu-trigger" aria-label={label} title={label}
      aria-haspopup="menu" aria-expanded={open} aria-controls={open ? id : undefined} disabled={busy}
      onClick={() => { lastFirst.current = false; setOpen(value => !value); }}><Icon size={20} aria-hidden="true" /></button>
    {open && <div ref={menu} id={id} role="menu" aria-labelledby={`${id}-trigger`} className="content-menu-panel">{children(close)}</div>}
  </div>;
}

export function ReportButton({ type, id, notify }: { type: number; id: string; notify: (s: string) => void }) {
  const [open, setOpen] = useState(false);
  return <><button className="text-button" onClick={() => setOpen(v => !v)}>Пожаловаться</button>
    {open && <ReportForm type={type} id={id} notify={notify} close={() => setOpen(false)} />}
  </>;
}

function ReportForm({ type, id, notify, close }: { type: number; id: string; notify: (s: string) => void; close: () => void }) {
  const [busy, setBusy] = useState(false);
  const [reason, setReason] = useState("");
  const textarea = useRef<HTMLTextAreaElement>(null);
  useEffect(() => { textarea.current?.focus(); }, []);
  return <form className="editor-form" onSubmit={async e => {
      e.preventDefault(); if (busy) return; setBusy(true);
      try { await api("/reports", { method: "POST", body: JSON.stringify({ targetType: type, targetId: id, reason }) }); close(); notify("Жалоба отправлена модератору"); }
      catch (e) { notify((e as Error).message); }
      finally { setBusy(false); }
    }}><label>Причина<textarea ref={textarea} required minLength={5} maxLength={2000} value={reason} onChange={e => setReason(e.target.value)} /></label>
      <div className="content-tools"><button className="button secondary" disabled={busy}>Отправить жалобу</button>
        <button type="button" className="text-button" disabled={busy} onClick={close}>Отмена</button></div></form>;
}
