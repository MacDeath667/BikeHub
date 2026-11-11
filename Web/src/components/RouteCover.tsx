import { lazy, Suspense, useEffect, useRef, useState } from "react";
import { api } from "../api";
import type { RouteData } from "../api";
import { Loading } from "./ui";
const RouteMap = lazy(() => import("./RouteMap"));

export function RouteCover({ route, previewUrl, href }: { route?: RouteData | null; previewUrl?: string; href?: string }) {
  const container = useRef<HTMLDivElement>(null);
  const [visible, setVisible] = useState(!!route), [preview, setPreview] = useState<RouteData | null>(null), [error, setError] = useState("");
  const [retry, setRetry] = useState(0);
  useEffect(() => {
    if (route || !container.current) return;
    const observer = new IntersectionObserver(entries => { if (entries.some(e => e.isIntersecting)) { setVisible(true); observer.disconnect(); } }, { rootMargin: "150px" });
    observer.observe(container.current); return () => observer.disconnect();
  }, [route]);
  useEffect(() => {
    if (!visible || route || !previewUrl) return;
    let active = true; setPreview(null); setError("");
    api<RouteData>(previewUrl).then(result => { if (active) setPreview(result); }).catch(e => { if (active) setError(e.message); });
    return () => { active = false; };
  }, [visible, route, previewUrl, retry]);
  const data = route || preview;
  return <div className={`route-cover${href ? " preview" : ""}`} ref={container}>
    {error ? <div className="route-cover-placeholder" role="status"><p>{error}</p><button type="button" className="text-button" onClick={() => setRetry(v => v + 1)}>Повторить</button></div> : visible && data ? <Suspense fallback={<Loading />}><RouteMap route={data} interactive={!href} /></Suspense> : <div className="route-cover-placeholder"><Loading /></div>}
    {href && <a href={href} className="route-cover-link">Открыть запись с маршрутом →</a>}
  </div>;
}
