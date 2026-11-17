import { useEffect, useRef, useState } from "react";
import { Monitor, Moon, Sun } from "lucide-react";

type Theme = "system" | "light" | "dark";
const storageKey = "bikehub.theme";
const themes = [
  { value: "light", label: "Светлая тема", icon: Sun },
  { value: "dark", label: "Тёмная тема", icon: Moon },
  { value: "system", label: "Как на устройстве", icon: Monitor },
] as const;
function readTheme(): Theme {
  try {
    const value = localStorage.getItem(storageKey);
    if (value === "light" || value === "dark") {
      return value;
    }
  } catch { /* System preference still works when storage is unavailable. */ }
  return "system";
}

export function ThemePicker() {
  const [theme, setTheme] = useState<Theme>(readTheme);
  const [open, setOpen] = useState(false);
  const picker = useRef<HTMLDivElement>(null), currentButton = useRef<HTMLButtonElement>(null), restoreFocus = useRef(false);
  useEffect(() => {
    if (!open) {
      if (restoreFocus.current) { currentButton.current?.focus(); restoreFocus.current = false; }
      return;
    }
    const outside = (event: PointerEvent) => {
      if (!picker.current?.contains(event.target as Node)) { setOpen(false); }
    };
    document.addEventListener("pointerdown", outside, true);
    return () => document.removeEventListener("pointerdown", outside, true);
  }, [open, theme]);
  useEffect(() => {
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    const apply = () => {
      const resolved = theme === "system" ? (media.matches ? "dark" : "light") : theme;
      document.documentElement.dataset.theme = resolved;
      document.querySelector('meta[name="theme-color"]')?.setAttribute("content", resolved === "dark" ? "#111a16" : "#f6f7f4");
    };
    apply();
    media.addEventListener("change", apply);
    return () => media.removeEventListener("change", apply);
  }, [theme]);
  useEffect(() => {
    const sync = (event: StorageEvent) => {
      if (event.key === storageKey || event.key === null) {
        setTheme(readTheme());
      }
    };
    window.addEventListener("storage", sync);
    return () => window.removeEventListener("storage", sync);
  }, []);
  const choose = (next: Theme) => {
    try {
      if (next === "system") {
        localStorage.removeItem(storageKey);
      } else {
        localStorage.setItem(storageKey, next);
      }
    } catch { /* Keep the selection for this tab without persistent storage. */ }
    restoreFocus.current = true; setTheme(next); setOpen(false);
  };
  return <div ref={picker} className="theme-picker" role="group" aria-label="Цветовая тема"
    onBlur={event => { if (!event.currentTarget.contains(event.relatedTarget)) { setOpen(false); } }}
    onKeyDown={event => {
      if (open && event.key === "Escape") { event.preventDefault(); event.stopPropagation(); restoreFocus.current = true; setOpen(false); }
      if (["ArrowLeft", "ArrowRight", "ArrowDown", "ArrowUp", "Home", "End"].includes(event.key)) {
        event.preventDefault();
        if (!open) { setOpen(true); return; }
        const buttons = Array.from(event.currentTarget.querySelectorAll("button"));
        const index = buttons.indexOf(document.activeElement as HTMLButtonElement);
        const next = event.key === "Home" ? 0 : event.key === "End" ? buttons.length - 1 :
          (index + (event.key === "ArrowLeft" || event.key === "ArrowUp" ? buttons.length - 1 : 1)) % buttons.length;
        buttons[next]?.focus();
      }
    }}>
    {themes.filter(option => open || option.value === theme).map(({ value, label, icon: Icon }) => <button
      key={value}
      ref={value === theme ? currentButton : undefined}
      type="button"
      aria-label={label}
      title={open ? label : `${label}. Изменить тему`}
      aria-pressed={theme === value}
      aria-expanded={theme === value ? open : undefined}
      onClick={() => { if (open) { choose(value); } else { currentButton.current?.focus(); setOpen(true); } }}
    >
      <Icon size={18} strokeWidth={1.8} aria-hidden="true" />
    </button>)}
  </div>;
}
