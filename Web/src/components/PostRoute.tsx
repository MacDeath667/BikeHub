import { lazy, Suspense, useState } from "react";
import { api } from "../api";
import type { GeoPoint, RouteData } from "../api";
import { ErrorBox, Loading } from "./ui";
const RouteMap = lazy(() => import("./RouteMap"));
const emptyRoute = (): RouteData => ({ name: "Мой маршрут", segments: [], markers: [] });
function distance(route: RouteData) {
  let meters = 0;
  for (const segment of route.segments) for (let i = 1; i < segment.length; i++) {
    const a = segment[i - 1], b = segment[i], rad = Math.PI / 180;
    const h = Math.sin((b.lat - a.lat) * rad / 2) ** 2 + Math.cos(a.lat * rad) * Math.cos(b.lat * rad) * Math.sin((b.lng - a.lng) * rad / 2) ** 2;
    meters += 6371000 * 2 * Math.asin(Math.sqrt(Math.min(1, h)));
  }
  return (meters / 1000).toLocaleString("ru-RU", { maximumFractionDigits: 1 });
}
function download(route: RouteData) {
  const escape = (text: string) => text.replaceAll("&", "&amp;").replaceAll("<", "&lt;").replaceAll(">", "&gt;").replaceAll('"', "&quot;").replaceAll("'", "&apos;");
  const xml = `<?xml version="1.0" encoding="UTF-8"?><gpx xmlns="http://www.topografix.com/GPX/1/1" version="1.1" creator="BikeHub"><metadata><name>${escape(route.name)}</name></metadata>${route.markers.map(p => `<wpt lat="${p.lat}" lon="${p.lng}"><name>${escape(p.label)}</name></wpt>`).join("")}<trk><name>${escape(route.name)}</name>${route.segments.map(s => `<trkseg>${s.map(p => `<trkpt lat="${p.lat}" lon="${p.lng}"/>`).join("")}</trkseg>`).join("")}</trk></gpx>`;
  const url = URL.createObjectURL(new Blob([xml], { type: "application/gpx+xml" }));
  const link = document.createElement("a"); link.href = url; link.download = "bikehub-route.gpx"; link.click();
  window.setTimeout(() => URL.revokeObjectURL(url), 1000);
}
export function PostRoute({ route }: { route?: RouteData | null }) {
  const [open, setOpen] = useState(false);
  if (!route) return null;
  return <section className="post-route"><h2>{route.name}</h2><p className="form-hint">{distance(route)} км по линии · {route.markers.length} меток</p>
    {open ? <Suspense fallback={<Loading />}><RouteMap route={route} /></Suspense> : <button type="button" className="button secondary" onClick={() => setOpen(true)}>Показать маршрут на карте</button>}
    <button type="button" className="text-button" onClick={() => download(route)}>Скачать GPX</button>
    {route.markers.length > 0 && <ol className="route-waypoints">{route.markers.map((p, i) => <li key={i}>{p.label || `Точка ${i + 1}`} <span className="muted">{p.lat.toFixed(5)}, {p.lng.toFixed(5)}</span></li>)}</ol>}
  </section>;
}
export function RouteEditor({ value, onChange, disabled, onBusy }: { value: RouteData | null; onChange: (route: RouteData | null) => void; disabled: boolean; onBusy: (busy: boolean) => void }) {
  const [mode, setMode] = useState<"gpx" | "manual">(value?.segments.length ? "gpx" : "manual");
  const [editing, setEditing] = useState(!!value), [busy, setBusy] = useState(false), [error, setError] = useState("");
  const [lat, setLat] = useState("55.75"), [lng, setLng] = useState("37.62");
  const route = value || emptyRoute();
  function manual(markers: RouteData["markers"]) {
    onChange({ ...route, markers, segments: markers.length >= 2 ? [markers.map(({ lat, lng }) => ({ lat, lng }))] : [] });
  }
  function add(point: GeoPoint) {
    if (disabled || busy || mode !== "manual") return;
    if (route.markers.length >= 100) { setError("Можно добавить до 100 меток."); return; }
    if (!Number.isFinite(point.lat) || !Number.isFinite(point.lng) || Math.abs(point.lat) > 90 || Math.abs(point.lng) > 180) { setError("Проверьте широту и долготу."); return; }
    setError(""); manual([...route.markers, { ...point, label: `Точка ${route.markers.length + 1}` }]);
  }
  return <section className="route-editor"><div className="content-tools"><strong>Маршрут и точки</strong><button type="button" className="text-button" disabled={disabled || busy} onClick={() => setEditing(!editing)}>{editing ? "Свернуть" : value ? "Редактировать карту" : "Добавить карту"}</button>
    {value && <button type="button" className="text-button danger" disabled={disabled || busy} onClick={() => { onChange(null); setEditing(false); setError(""); }}>Убрать маршрут</button>}</div>
    {editing && <><div className="content-tools">{(["gpx", "manual"] as const).map(m => <button key={m} type="button" className={`button ${mode === m ? "primary" : "secondary"}`} disabled={disabled || busy} onClick={() => {
      if (m === mode) return;
      if (value && !window.confirm("Сменить способ добавления? Текущий маршрут будет очищен.")) return;
      setMode(m); onChange(null); setError("");
    }}>{m === "gpx" ? "Загрузить GPX" : "Поставить точки"}</button>)}</div>
      {mode === "gpx" && <label>GPX-файл<input type="file" accept=".gpx,application/gpx+xml" disabled={disabled || busy} onChange={async event => {
        const file = event.currentTarget.files?.[0]; event.currentTarget.value = ""; if (!file) return;
        if (file.size > 5 * 1024 * 1024) { setError("Выберите GPX-файл до 5 МБ."); return; }
        setBusy(true); onBusy(true); setError(""); try { const body = new FormData(); body.append("file", file); onChange(await api<RouteData>("/routes/import", { method: "POST", body })); }
        catch (e) { setError((e as Error).message); } finally { setBusy(false); onBusy(false); }
      }} /><span className="form-hint">До 5 МБ и 30 000 точек. Перед публикацией проверьте, нет ли на маршруте вашего домашнего адреса.</span></label>}
      {value && <label>Название маршрута<input maxLength={120} required disabled={disabled || busy} value={route.name} onChange={e => onChange({ ...route, name: e.target.value })} /></label>}
      {mode === "manual" && <><p className="form-hint">Нажимайте на карту или добавляйте координаты ниже. Точки соединяются прямыми линиями, без построения пути по дорогам.</p><div className="route-coordinate-inputs"><label>Широта<input type="number" min={-90} max={90} step="any" value={lat} disabled={disabled} onChange={e => setLat(e.target.value)} onKeyDown={e => { if (e.key === "Enter") { e.preventDefault(); add({ lat: Number(lat), lng: Number(lng) }); } }} /></label>
        <label>Долгота<input type="number" min={-180} max={180} step="any" value={lng} disabled={disabled} onChange={e => setLng(e.target.value)} onKeyDown={e => { if (e.key === "Enter") { e.preventDefault(); add({ lat: Number(lat), lng: Number(lng) }); } }} /></label>
        <button type="button" className="button secondary" disabled={disabled || !lat.trim() || !lng.trim()} onClick={() => add({ lat: Number(lat), lng: Number(lng) })}>Добавить точку</button></div></>}
      {(mode === "manual" || value) && <Suspense fallback={<Loading />}><RouteMap route={route} onPoint={mode === "manual" && !disabled ? add : undefined} /></Suspense>}
      {mode === "manual" && route.markers.map((p, i) => <div className="route-marker-editor" key={i}><span>{i + 1}.</span><input aria-label={`Название точки ${i + 1}`} maxLength={120} value={p.label} disabled={disabled} onChange={e => manual(route.markers.map((v, j) => j === i ? { ...v, label: e.target.value } : v))} /><small>{p.lat.toFixed(5)}, {p.lng.toFixed(5)}</small><button type="button" className="text-button" aria-label={`Удалить точку ${i + 1}`} disabled={disabled} onClick={() => manual(route.markers.filter((_, j) => j !== i))}>×</button></div>)}
      {busy && <p role="status">Читаем GPX…</p>}{error && <ErrorBox message={error} />}
    </>}
  </section>;
}
