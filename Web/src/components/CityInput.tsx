import { useEffect, useId, useRef, useState } from "react";
import type { ComponentPropsWithoutRef } from "react";
import { api } from "../api";
import type { CityOption } from "../api";

type Props = Omit<ComponentPropsWithoutRef<"input">, "value" | "onChange" | "defaultValue"> & {
  value: string;
  onValueChange: (value: string) => void;
};

export function CityInput({ value, onValueChange, ...field }: Props) {
  const id = useId(), input = useRef<HTMLInputElement>(null);
  const [focused, setFocused] = useState(false), [dismissed, setDismissed] = useState(false);
  const [options, setOptions] = useState<CityOption[]>([]), [selected, setSelected] = useState(-1);
  const [loading, setLoading] = useState(false), [error, setError] = useState(""), [retry, setRetry] = useState(0);
  useEffect(() => {
    setOptions([]); setSelected(-1); setError("");
    if (!focused || dismissed || field.disabled) { setLoading(false); return; }
    const controller = new AbortController();
    setLoading(true);
    const timer = setTimeout(() => {
      api<CityOption[]>(`/cities?q=${encodeURIComponent(value.trim())}`, { signal: controller.signal })
        .then(cities => { if (!controller.signal.aborted) { setOptions(cities); } })
        .catch(() => { if (!controller.signal.aborted) { setError("Не удалось загрузить города."); } })
        .finally(() => { if (!controller.signal.aborted) { setLoading(false); } });
    }, 180);
    return () => { controller.abort(); clearTimeout(timer); };
  }, [value, focused, dismissed, field.disabled, retry]);
  const open = focused && !dismissed && !field.disabled;
  const choose = (city: CityOption) => { input.current?.focus(); onValueChange(city.label); setDismissed(true); setSelected(-1); };
  return <span className="city-field">
    <input {...field} ref={input} type="text" role="combobox" autoComplete="off" maxLength={100} aria-label={field["aria-label"] || "Город"}
      value={value} aria-autocomplete="list" aria-expanded={open} aria-controls={open ? id : undefined}
      aria-activedescendant={open && selected >= 0 && options[selected] ? `${id}-${options[selected].id}` : undefined}
      onChange={event => { onValueChange(event.currentTarget.value); setDismissed(false); }}
      onFocus={event => { setFocused(true); setDismissed(false); field.onFocus?.(event); }}
      onBlur={event => { setFocused(false); field.onBlur?.(event); }}
      onKeyDown={event => {
        field.onKeyDown?.(event);
        if (event.defaultPrevented) { return; }
        if (event.key === "Escape" && open) { event.preventDefault(); event.stopPropagation(); setDismissed(true); }
        if (event.key === "ArrowDown" || event.key === "ArrowUp") {
          event.preventDefault(); setDismissed(false);
          if (options.length) { setSelected(index => index < 0 ? (event.key === "ArrowDown" ? 0 : options.length - 1) : (index + (event.key === "ArrowDown" ? 1 : options.length - 1)) % options.length); }
        }
        if (event.key === "Enter" && open && selected >= 0 && options[selected]) { event.preventDefault(); choose(options[selected]); }
      }} />
    {open && <span className="city-suggestions">
      <span id={id} role="listbox" aria-label="Города" aria-busy={loading}>
        {options.map((city, index) => <span id={`${id}-${city.id}`} key={city.id} role="option" aria-selected={selected === index}
          onPointerDown={event => event.preventDefault()} onClick={() => choose(city)}>
          <strong>{city.name}</strong><small>{city.region}</small>
        </span>)}
      </span>
      {loading && <span className="city-hint" role="status">Ищем города…</span>}
      {error && <span className="city-hint" role="alert">{error} <button type="button" className="text-button" onPointerDown={event => event.preventDefault()} onClick={() => setRetry(value => value + 1)}>Повторить</button></span>}
      {!loading && !error && !options.length && <span className="city-hint" role="status">Город не найден. Проверьте название или регион.</span>}
      <span className="city-attribution">Справочник: <a href="https://github.com/hflabs/city" target="_blank" rel="noreferrer" onPointerDown={event => event.preventDefault()}>HFLabs</a> · <a href="https://creativecommons.org/licenses/by-sa/4.0/" target="_blank" rel="noreferrer" onPointerDown={event => event.preventDefault()}>CC BY-SA 4.0</a></span>
    </span>}
  </span>;
}
