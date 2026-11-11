import { useEffect, useState } from "react";
import { Monitor, Moon, Sun } from "lucide-react";

type Theme = "system" | "light" | "dark";
const storageKey = "bikehub.theme";
function readTheme(): Theme {
  try {
    const value = localStorage.getItem(storageKey);
    if (value === "light" || value === "dark") return value;
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
      if (event.key === storageKey || event.key === null) setTheme(readTheme());
    };
    window.addEventListener("storage", sync);
    return () => window.removeEventListener("storage", sync);
  }, []);
  const Icon = theme === "system" ? Monitor : theme === "dark" ? Moon : Sun;
  return <label className="theme-picker" title="Цветовая тема">
    <Icon size={18} aria-hidden="true" />
    <select aria-label="Цветовая тема" value={theme} onChange={event => {
      const next = event.target.value as Theme;
      try {
        if (next === "system") localStorage.removeItem(storageKey);
        else localStorage.setItem(storageKey, next);
      } catch { /* Keep the selection for this tab without persistent storage. */ }
      setTheme(next);
    }}>
      <option value="system">Как на устройстве</option>
      <option value="light">Светлая</option>
      <option value="dark">Тёмная</option>
    </select>
  </label>;
}
