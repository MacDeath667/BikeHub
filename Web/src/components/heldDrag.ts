// A short touch remains a tap; motion before the hold cancels moving the pin.
export function attachHeldDrag(element: HTMLElement, callbacks: {
  lock: () => void; unlock: () => void;
  move: (x: number, y: number) => void; finish: () => void; cancel: () => void;
}, holdMilliseconds = 350) {
  let pointer: number | null = null, active = false, timer: ReturnType<typeof setTimeout> | undefined;
  let origin = { x: 0, y: 0 };
  function release(commit: boolean) {
    if (pointer === null) return;
    const id = pointer; pointer = null; clearTimeout(timer);
    if (element.hasPointerCapture(id)) element.releasePointerCapture(id);
    element.classList.remove("moving"); callbacks.unlock();
    if (active) { if (commit) callbacks.finish(); else callbacks.cancel(); }
    active = false;
  }
  const down = (event: PointerEvent) => {
    if (pointer !== null || !event.isPrimary || event.button !== 0) return;
    event.preventDefault(); event.stopPropagation();
    pointer = event.pointerId; origin = { x: event.clientX, y: event.clientY };
    element.setPointerCapture(pointer); callbacks.lock();
    timer = setTimeout(() => { active = true; element.classList.add("moving"); }, holdMilliseconds);
  };
  const move = (event: PointerEvent) => {
    if (event.pointerId !== pointer) return;
    event.preventDefault(); event.stopPropagation();
    if (active) callbacks.move(event.clientX, event.clientY);
    else if (Math.hypot(event.clientX - origin.x, event.clientY - origin.y) > 8) release(false);
  };
  const up = (event: PointerEvent) => { if (event.pointerId === pointer) { event.stopPropagation(); release(true); } };
  const cancel = (event: PointerEvent) => { if (event.pointerId === pointer) release(false); };
  const suppress = (event: Event) => { event.preventDefault(); event.stopPropagation(); };
  element.addEventListener("pointerdown", down); element.addEventListener("pointermove", move);
  element.addEventListener("pointerup", up); element.addEventListener("pointercancel", cancel);
  element.addEventListener("lostpointercapture", cancel); element.addEventListener("contextmenu", suppress); element.addEventListener("click", suppress);
  return () => {
    release(false);
    element.removeEventListener("pointerdown", down); element.removeEventListener("pointermove", move);
    element.removeEventListener("pointerup", up); element.removeEventListener("pointercancel", cancel);
    element.removeEventListener("lostpointercapture", cancel); element.removeEventListener("contextmenu", suppress); element.removeEventListener("click", suppress);
  };
}
