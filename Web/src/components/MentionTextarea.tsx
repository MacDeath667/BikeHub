import { useEffect, useId, useRef, useState } from "react";
import type { ComponentPropsWithoutRef, RefObject } from "react";
import { api } from "../api";
import type { User } from "../api";
import { completeMention, mentionAtCursor } from "./mentions";

type Props = Omit<ComponentPropsWithoutRef<"textarea">, "value" | "onChange"> & {
  value: string; onValueChange: (value: string) => void; textareaRef?: RefObject<HTMLTextAreaElement | null>;
};
export function MentionTextarea({ value, onValueChange, textareaRef, ...field }: Props) {
  const localRef = useRef<HTMLTextAreaElement>(null), input = textareaRef ?? localRef;
  const id = useId();
  const [range, setRange] = useState<ReturnType<typeof mentionAtCursor>>(null), [matches, setMatches] = useState<User[]>([]), [selected, setSelected] = useState(0);
  const query = range?.query || "";
  useEffect(() => {
    let active = true; setMatches([]); setSelected(0);
    if (!query || field.disabled) { return; }
    const timer = setTimeout(() => {
      api<User[]>(`/users/nicknames?q=${encodeURIComponent(query)}`).then(users => { if (active) { setMatches(users); } }).catch(() => {});
    }, 200);
    return () => { active = false; clearTimeout(timer); };
  }, [query, field.disabled]);
  const open = !!range && matches.length > 0 && !field.disabled;
  function insert(user: User) {
    if (!range) { return; }
    const next = completeMention(value, range, user.handle);
    if (field.maxLength && next.value.length > field.maxLength) { return; }
    onValueChange(next.value); setRange(null);
    requestAnimationFrame(() => { input.current?.focus(); input.current?.setSelectionRange(next.cursor, next.cursor); });
  }
  return <span className="mention-field">
    <textarea {...field} ref={input} value={value} aria-autocomplete="list" aria-controls={open ? id : undefined} aria-activedescendant={open ? `${id}-${Math.min(selected, matches.length - 1)}` : undefined}
      onChange={event => { onValueChange(event.currentTarget.value); setRange(mentionAtCursor(event.currentTarget.value, event.currentTarget.selectionStart)); }}
      onSelect={event => { field.onSelect?.(event); setRange(mentionAtCursor(event.currentTarget.value, event.currentTarget.selectionStart)); }}
      onBlur={event => { field.onBlur?.(event); setRange(null); }}
      onKeyDown={event => {
        field.onKeyDown?.(event);
        if (event.defaultPrevented || !open) { return; }
        if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); setRange(null); }
        if (event.key === "ArrowDown" || event.key === "ArrowUp") { event.preventDefault(); setSelected(index => (index + (event.key === "ArrowDown" ? 1 : matches.length - 1)) % matches.length); }
        if (event.key === "Enter" && !event.shiftKey) { event.preventDefault(); insert(matches[Math.min(selected, matches.length - 1)]); }
      }} />
    {open && <span id={id} role="listbox" aria-label="Упомянуть пользователя" className="mention-suggestions">
      {matches.map((user, index) => <span key={user.id} id={`${id}-${index}`} role="option" aria-selected={index === selected} tabIndex={-1} onPointerDown={event => event.preventDefault()} onClick={() => insert(user)}>
        <strong>@{user.handle}</strong><small>{user.name}</small>
      </span>)}
    </span>}
  </span>;
}
