import { useEffect, useRef, useState } from "react";
import L from "leaflet";
import "leaflet/dist/leaflet.css";
import type { GeoPoint, RouteData } from "../api";

type Props = { route: RouteData; onPoint?: (point: GeoPoint) => void; };
export default function RouteMap({ route, onPoint }: Props) {
  const container = useRef<HTMLDivElement>(null);
  const map = useRef<L.Map | null>(null), drawing = useRef<L.LayerGroup | null>(null);
  const handler = useRef(onPoint); handler.current = onPoint;
  const fitted = useRef(false);
  const [tileError, setTileError] = useState(false);
  useEffect(() => {
    if (!container.current) return;
    const instance = L.map(container.current, { scrollWheelZoom: false, doubleClickZoom: false, preferCanvas: true }).setView([55.75, 37.62], 10);
    map.current = instance;
    const tiles = L.tileLayer("https://tile.openstreetmap.org/{z}/{x}/{y}.png", {
      maxZoom: 19,
      attribution: '&copy; <a href="https://www.openstreetmap.org/copyright" target="_blank" rel="noopener noreferrer">OpenStreetMap</a> contributors',
      referrerPolicy: "strict-origin-when-cross-origin",
    }).addTo(instance);
    tiles.on("tileerror", () => setTileError(true));
    drawing.current = L.layerGroup().addTo(instance);
    instance.on("click", event => handler.current?.({ lat: +event.latlng.lat.toFixed(6), lng: +event.latlng.wrap().lng.toFixed(6) }));
    const observer = new ResizeObserver(() => instance.invalidateSize({ pan: false })); observer.observe(container.current);
    fitted.current = false;
    return () => { observer.disconnect(); instance.remove(); map.current = null; drawing.current = null; };
  }, []);
  useEffect(() => {
    const instance = map.current, layer = drawing.current; if (!instance || !layer) return;
    layer.clearLayers();
    const bounds = L.latLngBounds([]);
    for (const segment of route.segments) {
      const points: L.LatLngTuple[] = segment.map(p => [p.lat, p.lng]);
      L.polyline(points, { color: "#386e49", weight: 5, opacity: 0.95 }).addTo(layer);
      points.forEach(p => bounds.extend(p));
    }
    route.markers.forEach((point, index) => {
      const label = document.createElement("span"); label.textContent = `${index + 1}. ${point.label}`;
      L.circleMarker([point.lat, point.lng], { radius: 7, color: "#213c2b", fillColor: "#d5eea0", fillOpacity: 1, weight: 2 })
        .bindTooltip(label).addTo(layer);
      bounds.extend([point.lat, point.lng]);
    });
    if (bounds.isValid() && (!onPoint || !fitted.current)) {
      instance.fitBounds(bounds, { padding: [24, 24], maxZoom: 15 }); fitted.current = true;
    }
  }, [route, !!onPoint]);
  return <div className="route-map-wrapper">
    <div className="route-map" ref={container} role="region" aria-label={onPoint ? "Карта: нажмите, чтобы добавить точку" : `Карта маршрута: ${route.name}`} />
    {tileError && <p className="form-hint" role="status">Подложка карты недоступна. Координаты маршрута доступны; попробуйте обновить страницу позже.</p>}
  </div>;
}
