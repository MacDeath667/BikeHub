import { useEffect, useRef, useState } from "react";
import type { PointerEvent, KeyboardEvent } from "react";
import { centerCrop, cropImageStyle, moveCrop, zoomCrop } from "./avatarCropGeometry";
import type { AvatarCrop } from "./avatarCropGeometry";

export function AvatarCropper({ file, onApply, onCancel, uploading }: {
  file: File; onApply: (file: File) => Promise<void>; onCancel: () => void; uploading: boolean;
}) {
  const root = useRef<HTMLElement>(null);
  const image = useRef<HTMLImageElement>(null), area = useRef<HTMLDivElement>(null), circle = useRef<HTMLDivElement>(null);
  const drag = useRef<{ id: number; x: number; y: number; crop: AvatarCrop } | null>(null);
  const [url, setUrl] = useState("");
  const [dimensions, setDimensions] = useState<{ width: number; height: number } | null>(null);
  const [crop, setCrop] = useState<AvatarCrop | null>(null);
  const [areaSize, setAreaSize] = useState(0), [previewSize, setPreviewSize] = useState(0);
  const [exporting, setExporting] = useState(false), [error, setError] = useState(""), [moving, setMoving] = useState(false);
  const busy = uploading || exporting;
  useEffect(() => { root.current?.scrollIntoView({ block: "nearest" }); area.current?.focus({ preventScroll: true }); }, []);
  useEffect(() => { const objectUrl = URL.createObjectURL(file); setUrl(objectUrl); return () => URL.revokeObjectURL(objectUrl); }, [file]);
  useEffect(() => {
    const measure = () => { setAreaSize(area.current?.clientWidth || 0); setPreviewSize(circle.current?.clientWidth || 0); };
    const observer = new ResizeObserver(measure);
    if (area.current) observer.observe(area.current); if (circle.current) observer.observe(circle.current);
    measure(); return () => observer.disconnect();
  }, []);
  function start(event: PointerEvent<HTMLDivElement>) {
    if (!crop || !dimensions || busy || drag.current || !event.isPrimary || event.button !== 0) return;
    event.preventDefault(); event.currentTarget.focus(); event.currentTarget.setPointerCapture(event.pointerId);
    drag.current = { id: event.pointerId, x: event.clientX, y: event.clientY, crop }; setMoving(true);
  }
  function move(event: PointerEvent<HTMLDivElement>) {
    const active = drag.current; if (!active || event.pointerId !== active.id || !dimensions || !areaSize || busy) return;
    event.preventDefault();
    setCrop(moveCrop(active.crop, dimensions.width, dimensions.height,
      -(event.clientX - active.x) * active.crop.size / areaSize, -(event.clientY - active.y) * active.crop.size / areaSize));
  }
  function finish(event: PointerEvent<HTMLDivElement>, cancel = false) {
    const active = drag.current; if (!active || event.pointerId !== active.id) return;
    drag.current = null; setMoving(false);
    if (cancel) setCrop(active.crop);
    if (event.currentTarget.hasPointerCapture(event.pointerId)) event.currentTarget.releasePointerCapture(event.pointerId);
  }
  function keyboard(event: KeyboardEvent<HTMLDivElement>) {
    if (!crop || !dimensions || busy || drag.current) return;
    const step = crop.size * (event.shiftKey ? 0.1 : 0.02);
    const delta: Record<string, [number, number]> = { ArrowLeft: [-step, 0], ArrowRight: [step, 0], ArrowUp: [0, -step], ArrowDown: [0, step] };
    if (delta[event.key]) { event.preventDefault(); setCrop(moveCrop(crop, dimensions.width, dimensions.height, ...delta[event.key])); }
  }
  async function apply() {
    if (!crop || !dimensions || !image.current || busy || moving) return;
    setExporting(true); setError("");
    try {
      const edge = Math.min(512, Math.max(1, Math.round(crop.size)));
      const canvas = document.createElement("canvas"); canvas.width = edge; canvas.height = edge;
      const context = canvas.getContext("2d"); if (!context) throw new Error("Не удалось подготовить аватарку. Попробуйте другой браузер.");
      context.imageSmoothingEnabled = true; context.imageSmoothingQuality = "high";
      context.drawImage(image.current, crop.x, crop.y, crop.size, crop.size, 0, 0, edge, edge);
      const blob = await new Promise<Blob>((resolve, reject) => canvas.toBlob(result => result ? resolve(result) : reject(new Error("Не удалось обрезать фотографию.")), "image/png"));
      await onApply(new File([blob], "avatar.png", { type: "image/png" }));
    } catch (error) { setError((error as Error).message); }
    finally { setExporting(false); }
  }
  return <section ref={root} className="avatar-cropper" aria-label="Кадрирование аватарки" aria-busy={busy || (!crop && !error)} onKeyDown={event => {
    if (event.key === "Escape") { event.preventDefault(); event.stopPropagation(); if (!busy) onCancel(); }
  }}>
    <div><h3>Выберите область аватарки</h3><p className="form-hint" id="avatar-crop-instructions">Перетащите фотографию внутри квадратной рамки и настройте масштаб. Круг справа показывает результат. Для точной настройки можно использовать клавиши со стрелками.</p></div>
    <div className="avatar-crop-layout">
      <div className={`avatar-crop-area${moving ? " moving" : ""}${busy ? " busy" : ""}`} ref={area} tabIndex={busy ? -1 : 0} role="group" aria-label="Квадратная область кадрирования" aria-describedby="avatar-crop-instructions"
        onContextMenu={event => event.preventDefault()} onPointerDown={start} onPointerMove={move} onPointerUp={event => finish(event)} onPointerCancel={event => finish(event, true)} onLostPointerCapture={event => finish(event, true)} onKeyDown={keyboard}>
        {url && <img ref={image} src={url} alt="" draggable={false} style={crop && dimensions ? cropImageStyle(crop, dimensions.width, dimensions.height, areaSize) : { visibility: "hidden" }} onLoad={event => {
          const { naturalWidth: width, naturalHeight: height } = event.currentTarget;
          if (!width || !height || width > 10000 || height > 10000 || width * height > 24000000) { setError("Выберите фотографию до 24 мегапикселей и не более 10 000 пикселей по стороне."); return; }
          setDimensions({ width, height }); setCrop(centerCrop(width, height));
        }} onError={() => setError("Не удалось прочитать фотографию. Выберите JPEG, PNG или WebP.")} />}
        {!crop && !error && <span className="avatar-crop-loading">Читаем фотографию…</span>}
        <span className="avatar-crop-grid" aria-hidden="true" />
      </div>
      <div className="avatar-crop-preview"><div className="avatar-crop-circle" ref={circle} role="img" aria-label="Круглое превью аватарки">{url && crop && dimensions && <img src={url} alt="" draggable={false} style={cropImageStyle(crop, dimensions.width, dimensions.height, previewSize)} />}</div><span>Превью</span></div>
    </div>
    <label className="avatar-crop-zoom">Масштаб<input type="range" min="1" max="4" step="0.05" disabled={!crop || busy || moving} value={crop && dimensions ? Math.min(dimensions.width, dimensions.height) / crop.size : 1} onChange={event => { if (crop && dimensions) setCrop(zoomCrop(crop, dimensions.width, dimensions.height, Number(event.target.value))); }} /></label>
    {error && <p className="auth-error" role="alert">{error}</p>}
    <div className="content-tools"><button type="button" className="button primary" disabled={!crop || busy || moving} onClick={() => void apply()}>{busy ? "Сохраняем аватарку…" : "Применить обрезку"}</button>
      <button type="button" className="text-button" disabled={busy} onClick={onCancel}>Отмена</button><button type="button" className="text-button" disabled={!dimensions || busy || moving} onClick={() => { if (dimensions) setCrop(centerCrop(dimensions.width, dimensions.height)); }}>Сбросить</button></div>
  </section>;
}
