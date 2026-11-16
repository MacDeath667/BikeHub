import { useEffect, useState } from "react";
import type { RefObject } from "react";

const mobileQuery = "(max-width: 700px)";
const focusable = 'a[href], button:not([disabled]), input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

export function useMobileMenu(open: boolean, close: () => void, sidebar: RefObject<HTMLElement | null>, trigger: RefObject<HTMLButtonElement | null>) {
  const [mobile, setMobile] = useState(() => window.matchMedia(mobileQuery).matches);
  useEffect(() => {
    const media = window.matchMedia(mobileQuery);
    const changed = () => { setMobile(media.matches); if (!media.matches) close(); };
    changed(); media.addEventListener("change", changed);
    return () => media.removeEventListener("change", changed);
  }, [close]);
  useEffect(() => {
    if (!mobile || !open) return;
    const element = sidebar.current, originalOverflow = document.body.style.overflow;
    document.body.style.overflow = "hidden";
    (element?.querySelector<HTMLElement>(focusable) || element)?.focus();
    const keyboard = (event: KeyboardEvent) => {
      if (event.key === "Escape") { event.preventDefault(); close(); return; }
      if (event.key !== "Tab" || !element) return;
      const items = Array.from(element.querySelectorAll<HTMLElement>(focusable)).filter(item => !item.hidden && !item.closest("[inert]"));
      const first = items[0], last = items.at(-1), current = document.activeElement;
      if (!first || !last) { event.preventDefault(); element.focus(); return; }
      if (event.shiftKey && (current === first || !element.contains(current))) { event.preventDefault(); last.focus(); }
      else if (!event.shiftKey && (current === last || !element.contains(current))) { event.preventDefault(); first.focus(); }
    };
    document.addEventListener("keydown", keyboard);
    return () => {
      document.removeEventListener("keydown", keyboard);
      document.body.style.overflow = originalOverflow;
      if (window.matchMedia(mobileQuery).matches) trigger.current?.focus({ preventScroll: true });
    };
  }, [mobile, open, close, sidebar, trigger]);
  return mobile;
}
