import { useEffect, useState } from "react";
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
    setTheme(next);
  };
  return <div className="theme-picker" role="group" aria-label="Цветовая тема">
    {themes.map(({ value, label, icon: Icon }) => <button
      key={value}
      type="button"
      aria-label={label}
      title={label}
      aria-pressed={theme === value}
      onClick={() => choose(value)}
    >
      <Icon size={18} strokeWidth={1.8} aria-hidden="true" />
    </button>)}
  </div>;
}
